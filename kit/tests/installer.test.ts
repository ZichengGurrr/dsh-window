/**
 * Tests for the installer: release asset selection and the
 * download/upgrade decision matrix, using fake host deps.
 * @module dsh-window/tests/installer
 */

import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { ensureInstalled, pickReleaseInfo, type InstallerDeps } from '../src/installer.js'
import type { ResolvedConfig } from '../src/config.js'

const config: ResolvedConfig = {
  autoInstall: true,
  createShortcut: true,
  installDir: 'C:\\fake\\install',
  shortcutName: 'DeepSeek Harness Window',
  repoSlug: 'ZichengGurrr/dsh-window',
}

const releaseBody = JSON.stringify({
  tag_name: 'v1.2.0',
  assets: [
    { name: 'DeepSeek-Harness-Window-v1.2.0-win-x64.zip', browser_download_url: 'https://example.com/slim.zip' },
    { name: 'DeepSeek-Harness-Window-portable-v1.2.0-win-x64.zip', browser_download_url: 'https://example.com/portable.zip' },
  ],
})

class FakeDeps implements InstallerDeps {
  files = new Map<string, string | Buffer>()
  fetched: string[] = []
  shortcutCalls = 0
  extractCalls = 0

  exists(path: string): boolean {
    return this.files.has(path)
  }

  mkdir(_dir: string): void {}

  readFile(path: string): string | null {
    const value = this.files.get(path)
    return typeof value === 'string' ? value : null
  }

  writeFile(path: string, data: string | Buffer): void {
    this.files.set(path, data)
  }

  rm(path: string): void {
    this.files.delete(path)
  }

  async fetchText(url: string): Promise<string> {
    assert.equal(url, 'https://api.github.com/repos/ZichengGurrr/dsh-window/releases/latest')
    return releaseBody
  }

  async fetchBytes(url: string): Promise<Buffer> {
    this.fetched.push(url)
    return Buffer.from('fake-zip-bytes')
  }

  async extractZip(_zipPath: string, _destDir: string): Promise<void> {
    this.extractCalls += 1
  }

  async createShortcut(_exePath: string, _workDir: string, _name: string): Promise<void> {
    this.shortcutCalls += 1
  }
}

describe('pickReleaseInfo', () => {
  it('prefers the slim zip over the portable zip', () => {
    const info = pickReleaseInfo(releaseBody)
    assert.equal(info.tag, 'v1.2.0')
    assert.equal(info.assetUrl, 'https://example.com/slim.zip')
  })

  it('falls back to the portable zip when no slim zip exists', () => {
    const body = JSON.stringify({
      tag_name: 'v1.0.0',
      assets: [{ name: 'DeepSeek-Harness-Window-portable-v1.0.0-win-x64.zip', browser_download_url: 'https://example.com/portable.zip' }],
    })
    assert.equal(pickReleaseInfo(body).assetUrl, 'https://example.com/portable.zip')
  })

  it('throws when the release has no zip asset', () => {
    const body = JSON.stringify({ tag_name: 'v1.0.0', assets: [] })
    assert.throws(() => pickReleaseInfo(body), /no \.zip asset/)
  })
})

describe('ensureInstalled', () => {
  it('downloads and extracts when the exe is missing', async () => {
    const deps = new FakeDeps()
    const result = await ensureInstalled(config, deps)
    assert.equal(result.downloaded, true)
    assert.equal(result.updated, false)
    assert.equal(result.version, 'v1.2.0')
    assert.equal(deps.fetched.length, 1)
    assert.equal(deps.extractCalls, 1)
    assert.equal(deps.shortcutCalls, 1)
  })

  it('upgrades when the installed version differs from the release', async () => {
    const deps = new FakeDeps()
    deps.files.set('C:\\fake\\install\\DeepSeek Harness Window.exe', 'exe')
    deps.files.set('C:\\fake\\install\\installed-version.txt', 'v1.1.0')
    const result = await ensureInstalled(config, deps)
    assert.equal(result.downloaded, true)
    assert.equal(result.updated, true)
    assert.equal(deps.extractCalls, 1)
    assert.equal(deps.files.get('C:\\fake\\install\\installed-version.txt'), 'v1.2.0')
  })

  it('skips the download when the version is current', async () => {
    const deps = new FakeDeps()
    deps.files.set('C:\\fake\\install\\DeepSeek Harness Window.exe', 'exe')
    deps.files.set('C:\\fake\\install\\installed-version.txt', 'v1.2.0')
    const result = await ensureInstalled(config, deps)
    assert.equal(result.downloaded, false)
    assert.equal(result.updated, false)
    assert.equal(deps.fetched.length, 0)
    assert.equal(deps.extractCalls, 0)
    assert.equal(deps.shortcutCalls, 1)
  })

  it('respects createShortcut: false', async () => {
    const deps = new FakeDeps()
    const result = await ensureInstalled({ ...config, createShortcut: false }, deps)
    assert.equal(result.shortcut, false)
    assert.equal(deps.shortcutCalls, 0)
  })
})
