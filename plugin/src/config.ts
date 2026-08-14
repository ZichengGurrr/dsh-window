/**
 * Serializable configuration and schema.
 * @module dsh-window/config
 */

import z from '@deepseek-ai/schemastery'

/** Plugin configuration supplied by the profile composition. */
export interface Config {
  /** Download the app zip and (re)create the shortcut when DSH activates. */
  autoInstall?: boolean
  /** Create/refresh the desktop shortcut named `shortcutName`. */
  createShortcut?: boolean
  /** Install directory; empty uses %LOCALAPPDATA%\Programs\dsh-window. */
  installDir?: string
  /** Desktop shortcut display name (no version). */
  shortcutName?: string
  /** `owner/repo` whose GitHub Releases provide the app zip. */
  repoSlug?: string
}

/** Configuration after defaults have been resolved. */
export interface ResolvedConfig {
  autoInstall: boolean
  createShortcut: boolean
  installDir: string
  shortcutName: string
  repoSlug: string
}

/** Default install directory under %LOCALAPPDATA%. */
export function defaultInstallDir(): string {
  const base = process.env.LOCALAPPDATA ?? ''
  if (base === '') return ''
  const sep = '\\'
  return [base, 'Programs', 'dsh-window'].join(sep)
}

/** Loader-visible configuration schema and defaults. */
export const Config: z<Config> = z.object({
  autoInstall: z.boolean().default(true),
  createShortcut: z.boolean().default(true),
  installDir: z.string().default(defaultInstallDir()),
  shortcutName: z.string().default('DeepSeek Harness Window'),
  repoSlug: z.string().default('ZichengGurrr/dsh-window'),
})

/**
 * Resolve defaults for direct callers that bypass Cordis Loader.
 * @param config - Partial serialized configuration.
 * @returns Configuration with all defaults applied.
 */
export function resolveConfig(config: Config = {}): ResolvedConfig {
  return {
    autoInstall: config.autoInstall ?? true,
    createShortcut: config.createShortcut ?? true,
    installDir: config.installDir ?? defaultInstallDir(),
    shortcutName: config.shortcutName ?? 'DeepSeek Harness Window',
    repoSlug: config.repoSlug ?? 'ZichengGurrr/dsh-window',
  }
}
