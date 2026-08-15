/**
 * Detached launch of the desktop exe.
 * @module dsh-window/launcher
 */
/**
 * Start the desktop app detached so it outlives this DSH host process.
 * @param exePath - absolute path to DeepSeek Harness Window.exe.
 * @returns the spawned child (already unref'd).
 */
export declare function launchDesktop(exePath: string): import("child_process").ChildProcess;
