## ADDED Requirements

### Requirement: Split lock architecture for ConfigurationService
ConfigurationService SHALL use two separate synchronization primitives:
1. A SemaphoreSlim (_fileLock) for file I/O operations (disk reads and writes)
2. A Lock (_stateLock) for in-memory state access (_pendingConfig, _mainConfigCache, _lastKnownGoodConfig)

#### Scenario: Concurrent config read and file write
- **WHEN** one thread is writing config to disk (holding _fileLock)
- **AND** another thread reads the in-memory cached config
- **THEN** the read SHALL succeed without blocking on the file lock
- **AND** the read SHALL return a consistent snapshot of the cached config

#### Scenario: Concurrent in-memory state updates
- **WHEN** two threads simultaneously update in-memory config state
- **THEN** the _stateLock SHALL serialize the updates
- **AND** no data corruption or lost updates SHALL occur

### Requirement: Clone-on-read for UserConfiguration
All public methods that return UserConfiguration SHALL return a deep clone, never the cached reference.

#### Scenario: Caller mutates returned config
- **WHEN** a caller receives a UserConfiguration from LoadUserConfigAsync
- **AND** the caller modifies a property on the returned object
- **THEN** the internal cached config SHALL NOT be affected
- **AND** subsequent calls to LoadUserConfigAsync SHALL return the original values

#### Scenario: Deep clone includes nested collections
- **WHEN** LoadUserConfigAsync returns a UserConfiguration clone
- **THEN** the SkipLists dictionary SHALL be a separate instance from the cached copy
- **AND** the GameDataFolderOverrides dictionary SHALL be a separate instance
- **AND** modifying a cloned collection SHALL NOT affect the cached version

### Requirement: Remove dead code UpdateMultipleAsync
The UpdateMultipleAsync method SHALL be removed from both IConfigurationService interface and ConfigurationService implementation.

#### Scenario: Interface no longer exposes UpdateMultipleAsync
- **WHEN** a developer examines IConfigurationService
- **THEN** UpdateMultipleAsync SHALL NOT be present in the interface

### Requirement: Safe debounce save pipeline
The Rx save pipeline SHALL properly await async save operations instead of using fire-and-forget.

#### Scenario: Save pipeline error handling
- **WHEN** a debounced save operation fails
- **THEN** the error SHALL be caught and logged by the Rx pipeline error handler
- **AND** the error SHALL NOT become an unobserved task exception

#### Scenario: Superseded save is cancelled
- **WHEN** multiple save requests arrive within the 500ms throttle window
- **THEN** only the most recent config SHALL be saved to disk
- **AND** any in-progress save for an older config SHALL be cancelled

### Requirement: Atomic pending config management
The _pendingConfig field SHALL be read into a local variable before null-checking and returning, preventing TOCTOU races.

#### Scenario: Concurrent read and clear of pending config
- **WHEN** one thread reads _pendingConfig in LoadUserConfigAsync
- **AND** another thread clears _pendingConfig in SaveToDiskWithRetryAsync
- **THEN** the reading thread SHALL either see the pending config or null consistently
- **AND** no NullReferenceException SHALL occur

### Requirement: Thread-safe main config cache access
The _mainConfigCache field SHALL only be read and written under _stateLock protection (or via atomic operations).

#### Scenario: Concurrent main config cache initialization
- **WHEN** two threads simultaneously call LoadMainConfigAsync
- **AND** _mainConfigCache is null
- **THEN** only one thread SHALL perform the file read
- **AND** the second thread SHALL receive the cached result

### Requirement: Missing CancellationToken parameters
GetSkipListAsync, GetDefaultSkipListAsync, and GetXEditExecutableNamesAsync SHALL accept an optional CancellationToken parameter.

#### Scenario: Cancellation of skip list retrieval
- **WHEN** GetSkipListAsync is called with a cancelled CancellationToken
- **THEN** the method SHALL throw OperationCanceledException
