/**
 * Serializable configuration and schema.
 * @module dsh-window/config
 */
import z from '@deepseek-ai/schemastery';
/** Plugin configuration supplied by the profile composition. */
export interface Config {
    /** Download the app zip and (re)create the shortcut when DSH activates. */
    autoInstall?: boolean;
    /** Create/refresh the desktop shortcut named `shortcutName`. */
    createShortcut?: boolean;
    /** Install directory; empty uses %LOCALAPPDATA%\Programs\dsh-window. */
    installDir?: string;
    /** Desktop shortcut display name (no version). */
    shortcutName?: string;
    /** `owner/repo` whose GitHub Releases provide the app zip. */
    repoSlug?: string;
}
/** Configuration after defaults have been resolved. */
export interface ResolvedConfig {
    autoInstall: boolean;
    createShortcut: boolean;
    installDir: string;
    shortcutName: string;
    repoSlug: string;
}
/** Default install directory under %LOCALAPPDATA%. */
export declare function defaultInstallDir(): string;
/** Loader-visible configuration schema and defaults. */
export declare const Config: z<Config>;
/**
 * Resolve defaults for direct callers that bypass Cordis Loader.
 * @param config - Partial serialized configuration.
 * @returns Configuration with all defaults applied.
 */
export declare function resolveConfig(config?: Config): ResolvedConfig;
