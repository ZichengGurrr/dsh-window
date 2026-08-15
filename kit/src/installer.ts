/**
 * Idempotent install of the dsh-window app zip plus desktop shortcut.
 * The app ships as a zip (exe + WebView2 runtime files); unlike a bare
 * exe, it needs extraction before the shortcut can point at it.
 * @module dsh-window/installer
 */

import { spawn } from 'node:child_process'
import * as fs from 'node:fs'
import type { ResolvedConfig } from './config.js'

/** Fakeable host boundary; every effect the installer can take. */
export interface InstallerDeps {
  exists(path: string): boolean
  mkdir(dir: string): void
  readFile(path: string): string | null
  writeFile(path: string, data: string | Buffer): void
  rm(path: string): void
  /** Fetch a URL's body as JSON text (GitHub API). */
  fetchText(url: string): Promise<string>
  /** Fetch a URL's body as bytes (release asset). */
  fetchBytes(url: string): Promise<Buffer>
  /** Extract a zip archive into a destination directory. */
  extractZip(zipPath: string, destDir: string): Promise<void>
  /** Create/refresh a desktop .lnk pointing at the exe. */
  createShortcut(exePath: string, workDir: string, name: string): Promise<void>
}

/** Outcome of one ensureInstalled run. */
export interface InstallResult {
  exePath: string
  /** Bytes for the zip were downloaded during this run. */
  downloaded: boolean
  /** The installed version changed (fresh install or upgrade). */
  updated: boolean
  /** The desktop shortcut was created/refreshed. */
  shortcut: boolean
  /** Release tag now installed. */
  version: string
}

/** Parsed latest release: tag plus the slim zip asset URL. */
export interface ReleaseInfo {
  tag: string
  assetUrl: string
}

/** Single-quote escape for PowerShell string literals. */
function psQuote(value: string): string {
  return value.replaceAll('\'', '\'\'')
}

/**
 * Pick the release metadata for the slim zip from a GitHub release JSON body.
 * The portable zip (bundled Node/DSH/Git, ~180MB) is never used here: DSH
 * already runs on Node, so the slim build (4 files, ~256KB) is enough.
 * @param body - releases/latest JSON text.
 * @returns the tag and the slim zip's browser_download_url.
 * @throws when the release has no zip asset at all.
 */
export function pickReleaseInfo(body: string): ReleaseInfo {
  const release = JSON.parse(body) as {
    tag_name?: string
    assets?: Array<{ name: string, browser_download_url: string }>
  }
  const tag = release.tag_name ?? 'unknown'
  const assets = release.assets ?? []
  const slim = assets.find(candidate =>
    candidate.name.endsWith('.zip') && !candidate.name.toLowerCase().includes('portable'))
  const fallback = assets.find(candidate => candidate.name.endsWith('.zip'))
  const asset = slim ?? fallback
  if (asset === undefined) throw new Error('latest release has no .zip asset')
  return { tag, assetUrl: asset.browser_download_url }
}

/** Production deps over node:fs, global fetch, curl, and PowerShell. */
export function nodeDeps(): InstallerDeps {
  const tmpPrefix = `${process.env.TEMP ?? process.cwd()}\\dsh-window-download-${process.pid}-${Date.now()}`
  return {
    exists: path => fs.existsSync(path),
    mkdir: dir => fs.mkdirSync(dir, { recursive: true }),
    readFile: path => {
      try {
        return fs.readFileSync(path, 'utf8')
      } catch {
        return null
      }
    },
    writeFile: (path, data) => fs.writeFileSync(path, data),
    rm: path => fs.rmSync(path, { force: true }),
    // The API JSON is small and works over plain fetch.
    fetchText: async url => {
      const response = await fetch(url, {
        headers: { 'User-Agent': 'dsh-window', Accept: 'application/vnd.github+json' },
        signal: AbortSignal.timeout(15_000),
      })
      if (!response.ok) throw new Error(`GitHub API ${response.status} for ${url}`)
      return response.text()
    },
    // Release assets are multi-MB; Node's fetch stalls on some networks where
    // system curl succeeds, so route the binary download through curl.exe.
    fetchBytes: url => new Promise((resolve, reject) => {
      const tmp = `${tmpPrefix}.zip`
      const child = spawn('curl', [
        '--silent', '--show-error', '--location', '--fail', '--retry', '2',
        '--max-time', '150', '--user-agent', 'dsh-window', '--output', tmp, url,
      ], { stdio: 'ignore', windowsHide: true })
      child.on('error', error => { fs.rmSync(tmp, { force: true }); reject(error) })
      child.on('exit', code => {
        if (code !== 0) {
          fs.rmSync(tmp, { force: true })
          reject(new Error(`curl exit ${code} for ${url}`))
          return
        }
        try { resolve(fs.readFileSync(tmp)) } catch (error) { reject(error) } finally { fs.rmSync(tmp, { force: true }) }
      })
    }),
    extractZip: (zipPath, destDir) => new Promise((resolve, reject) => {
      const script = [
        "$ErrorActionPreference='Stop'",
        `Expand-Archive -LiteralPath '${psQuote(zipPath)}' -DestinationPath '${psQuote(destDir)}' -Force`,
      ].join('\n')
      const child = spawn('powershell', ['-NoProfile', '-Command', script], {
        stdio: 'ignore',
        windowsHide: true,
      })
      child.on('error', reject)
      child.on('exit', code => (code === 0 ? resolve() : reject(new Error(`extract exit ${code}`))))
    }),
    createShortcut: (exePath, workDir, name) => new Promise((resolve, reject) => {
      const script = [
        '$ws = New-Object -ComObject WScript.Shell',
        `$lnk = $ws.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) '${psQuote(name)}.lnk'))`,
        `$lnk.TargetPath = '${psQuote(exePath)}'`,
        `$lnk.WorkingDirectory = '${psQuote(workDir)}'`,
        `$lnk.IconLocation = '${psQuote(exePath)},0'`,
        '$lnk.Save()',
      ].join('\n')
      const child = spawn('powershell', ['-NoProfile', '-Command', script], {
        stdio: 'ignore',
        windowsHide: true,
      })
      child.on('error', reject)
      child.on('exit', code => (code === 0 ? resolve() : reject(new Error(`shortcut exit ${code}`))))
    }),
  }
}

/**
 * Ensure the app is installed (downloading the slim zip from the repo's
 * latest GitHub Release when missing or outdated) and the desktop shortcut
 * points at the exe. Safe to re-run; upgrades happen when the release tag
 * differs from the locally recorded one.
 * @param config - resolved plugin configuration.
 * @param deps - host boundary to fake in tests.
 * @returns what happened during this run.
 */
export async function ensureInstalled(config: ResolvedConfig, deps: InstallerDeps): Promise<InstallResult> {
  const exePath = `${config.installDir}\\DeepSeek Harness Window.exe`
  const versionPath = `${config.installDir}\\installed-version.txt`
  const body = await deps.fetchText(`https://api.github.com/repos/${config.repoSlug}/releases/latest`)
  const release = pickReleaseInfo(body)
  const installedVersion = (deps.readFile(versionPath) ?? '').trim()
  const installedMissing = !deps.exists(exePath)
  const installedOutdated = installedVersion !== release.tag

  let downloaded = false
  if (installedMissing || installedOutdated) {
    const bytes = await deps.fetchBytes(release.assetUrl)
    const tmpZip = `${config.installDir}\\.download-${process.pid}-${Date.now()}.zip`
    deps.mkdir(config.installDir)
    deps.writeFile(tmpZip, bytes)
    try {
      await deps.extractZip(tmpZip, config.installDir)
    } finally {
      deps.rm(tmpZip)
    }
    deps.writeFile(versionPath, release.tag)
    downloaded = true
  }

  let shortcut = false
  if (config.createShortcut) {
    await deps.createShortcut(exePath, config.installDir, config.shortcutName)
    shortcut = true
  }
  return { exePath, downloaded, updated: downloaded && !installedMissing, shortcut, version: release.tag }
}
