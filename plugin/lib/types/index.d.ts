/**
 * dsh-window: install and launch the DeepSeek Harness Windows desktop
 * shell (WebView2) from a DSH profile.
 * @module dsh-window
 */
/** Cordis plugin name; keep this stable after publishing. */
export declare const name = "dsh-window";
/** Services required before load: the model-facing tool registry. */
export declare const inject: string[];
export { Config } from './config.js';
export type { ResolvedConfig } from './config.js';
export { apply } from './runtime.js';
