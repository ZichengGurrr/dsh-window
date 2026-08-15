/**
 * Serializable configuration and schema.
 * @module dsh-window/config
 */
import z from '@deepseek-ai/schemastery';
/** Default install directory under %LOCALAPPDATA%. */
export function defaultInstallDir() {
    const base = process.env.LOCALAPPDATA ?? '';
    if (base === '')
        return '';
    const sep = '\\';
    return [base, 'Programs', 'dsh-window'].join(sep);
}
/** Loader-visible configuration schema and defaults. */
export const Config = z.object({
    autoInstall: z.boolean().default(true),
    createShortcut: z.boolean().default(true),
    installDir: z.string().default(defaultInstallDir()),
    shortcutName: z.string().default('DeepSeek Harness Window'),
    repoSlug: z.string().default('ZichengGurrr/dsh-window'),
});
/**
 * Resolve defaults for direct callers that bypass Cordis Loader.
 * @param config - Partial serialized configuration.
 * @returns Configuration with all defaults applied.
 */
export function resolveConfig(config = {}) {
    return {
        autoInstall: config.autoInstall ?? true,
        createShortcut: config.createShortcut ?? true,
        installDir: config.installDir ?? defaultInstallDir(),
        shortcutName: config.shortcutName ?? 'DeepSeek Harness Window',
        repoSlug: config.repoSlug ?? 'ZichengGurrr/dsh-window',
    };
}
