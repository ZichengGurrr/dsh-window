/**
 * Cordis activation: auto-install on load plus the desktop_launch tool.
 * @module dsh-window/runtime
 */

import type { Context } from '@deepseek-ai/cordis'
import { defineTool } from '@deepseek-ai/dsh-tools'
import { resolveConfig, type Config } from './config.js'
import { ensureInstalled, nodeDeps } from './installer.js'
import { launchDesktop } from './launcher.js'

/**
 * Apply the plugin to its Cordis context.
 * @param ctx - Scoped plugin context; the tool registration is owned by it.
 * @param config - Configuration resolved by Cordis from the exported schema.
 */
export function apply(ctx: Context, config?: Config): void {
  const resolved = resolveConfig(config)
  const logger = ctx.logger

  if (process.platform !== 'win32') {
    logger.info('dsh-window: non-Windows host, staying idle')
  } else if (resolved.autoInstall) {
    // Install runs detached from activation so a slow or failing download
    // never blocks DSH startup; the tool re-runs it on demand.
    void ensureInstalled(resolved, nodeDeps())
      .then(result => {
        logger.info(
          `dsh-window: app ready at ${result.exePath}`
          + `${result.downloaded ? ` (downloaded ${result.version})` : ''}`
          + `${result.updated ? ' (updated)' : ''}${result.shortcut ? ', shortcut refreshed' : ''}`,
        )
      })
      .catch(error => { logger.warn(`dsh-window: install failed: ${String(error)}`) })
  }

  ctx.tools.register(defineTool({
    name: 'desktop_launch',
    description:
      'Launch the DeepSeek Harness desktop app (dsh-window, a native Windows WebView2 window around the webchat). '
      + 'Installs it first when missing or outdated: downloads the slim zip from GitHub Releases into %LOCALAPPDATA%\\Programs\\dsh-window '
      + 'and creates/refreshes the desktop shortcut. Use when the user wants to open or install the desktop app.',
    parameters: {},
    output: {
      schema: {
        type: 'object',
        properties: {
          launched: { type: 'boolean', description: 'Whether the desktop app was started.' },
          exePath: { type: 'string', description: 'Absolute path of the launched exe.' },
          version: { type: 'string', description: 'Installed release version.' },
        },
        additionalProperties: false,
      },
      render: (_args, value) => [{
        type: 'text',
        text: `DeepSeek Harness desktop app ${value.launched === true ? `launched: ${value.exePath} (${value.version})` : 'not launched (Windows only)'}`,
      }],
    },
    timeoutMs: 300_000,
    async execute() {
      if (process.platform !== 'win32') {
        return { launched: false, exePath: '', version: '' }
      }
      const result = await ensureInstalled(resolved, nodeDeps())
      launchDesktop(result.exePath)
      return { launched: true, exePath: result.exePath, version: result.version }
    },
  }))
}
