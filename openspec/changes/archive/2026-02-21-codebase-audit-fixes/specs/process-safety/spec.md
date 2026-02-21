## ADDED Requirements

### Requirement: Thread-safe process output capture
ProcessExecutionService SHALL use ConcurrentQueue instead of List for capturing stdout and stderr lines from process OutputDataReceived and ErrorDataReceived events.

#### Scenario: Concurrent output events
- **WHEN** multiple OutputDataReceived events fire simultaneously on thread pool threads
- **THEN** all output lines SHALL be captured without data loss or corruption
- **AND** the final ProcessResult.OutputLines SHALL contain all captured lines in order

### Requirement: Process handle disposal in orphan cleanup
Process objects obtained via Process.GetProcessById() in CleanOrphanedProcessesAsync SHALL be wrapped in using statements to prevent native handle leaks.

#### Scenario: Orphan detection with handle cleanup
- **WHEN** CleanOrphanedProcessesAsync checks tracked PIDs
- **AND** Process.GetProcessById() returns a Process object
- **THEN** the Process object SHALL be disposed after use regardless of whether it is an xEdit process

### Requirement: CancellationTokenSource proper linking
All CancellationTokenSource instances created for timeout handling SHALL be properly linked to the caller CancellationToken and disposed after use.

#### Scenario: Timeout CTS disposal
- **WHEN** ExecuteAsync creates a timeout CancellationTokenSource
- **THEN** the CTS SHALL be disposed via using statement when the method exits
- **AND** the linked CTS SHALL also be disposed
