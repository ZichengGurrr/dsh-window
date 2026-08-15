/**
 * dsh-window-kit: install and launch the DeepSeek Harness Windows desktop
 * shell (WebView2) from a DSH profile, bundled with the DeepEye vision
 * plugin and the voice input plugin.
 * @module dsh-window-kit
 */
/** Cordis plugin name; keep this stable after publishing. */
export declare const name = "dsh-window-kit";
/** Services required before load: the model-facing tool registry. */
export declare const inject: string[];
export { Config } from './config.js';
export type { ResolvedConfig } from './config.js';
export { apply } from './runtime.js';
