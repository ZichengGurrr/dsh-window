/**
 * Idempotent install of the dsh-window app zip plus desktop shortcut.
 * The app ships as a zip (exe + WebView2 runtime files); unlike a bare
 * exe, it needs extraction before the shortcut can point at it.
 * @module dsh-window/installer
 */
import type { ResolvedConfig } from './config.js';
/** Fakeable host boundary; every effect the installer can take. */
export interface InstallerDeps {
    exists(path: string): boolean;
    mkdir(dir: string): void;
    readFile(path: string): string | null;
    writeFile(path: string, data: string | Buffer): void;
    rm(path: string): void;
    /** Fetch a URL's body as JSON text (GitHub API). */
    fetchText(url: string): Promise<string>;
    /** Fetch a URL's body as bytes (release asset). */
    fetchBytes(url: string): Promise<Buffer>;
    /** Extract a zip archive into a destination directory. */
    extractZip(zipPath: string, destDir: string): Promise<void>;
    /** Create/refresh a desktop .lnk pointing at the exe. */
    createShortcut(exePath: string, workDir: string, name: string): Promise<void>;
}
/** Outcome of one ensureInstalled run. */
export interface InstallResult {
    exePath: string;
    /** Bytes for the zip were downloaded during this run. */
    downloaded: boolean;
    /** The installed version changed (fresh install or upgrade). */
    updated: boolean;
    /** The desktop shortcut was created/refreshed. */
    shortcut: boolean;
    /** Release tag now installed. */
    version: string;
}
/** Parsed latest release: tag plus the slim zip asset URL. */
export interface ReleaseInfo {
    tag: string;
    assetUrl: string;
}
/**
 * Pick the release metadata for the slim zip from a GitHub release JSON body.
 * The portable zip (bundled Node/DSH/Git, ~180MB) is never used here: DSH
 * already runs on Node, so the slim build (4 files, ~256KB) is enough.
 * @param body - releases/latest JSON text.
 * @returns the tag and the slim zip's browser_download_url.
 * @throws when the release has no zip asset at all.
 */
export declare function pickReleaseInfo(body: string): ReleaseInfo;
/** Production deps over node:fs, global fetch, curl, and PowerShell. */
export declare function nodeDeps(): InstallerDeps;
/**
 * Ensure the app is installed (downloading the slim zip from the repo's
 * latest GitHub Release when missing or outdated) and the desktop shortcut
 * points at the exe. Safe to re-run; upgrades happen when the release tag
 * differs from the locally recorded one.
 * @param config - resolved plugin configuration.
 * @param deps - host boundary to fake in tests.
 * @returns what happened during this run.
 */
export declare function ensureInstalled(config: ResolvedConfig, deps: InstallerDeps): Promise<InstallResult>;
