# resource-lifecycle Specification

## Purpose
Define disposal and subscription lifecycle requirements across app shutdown and views.

## Requirements

### Requirement: ServiceProvider disposal on application shutdown
The application SHALL dispose the DI ServiceProvider during shutdown, which cascades Dispose to all singleton IDisposable services.

#### Scenario: Clean shutdown disposes all services
- **WHEN** the application receives a shutdown request
- **THEN** it SHALL flush pending config saves
- **AND** it SHALL dispose the ServiceProvider
- **AND** all IDisposable singleton services SHALL have their Dispose method called

### Requirement: Serilog flush on shutdown
The application SHALL call Log.CloseAndFlush() during shutdown to ensure all buffered log entries are written.

#### Scenario: Log entries flushed before exit
- **WHEN** the application shuts down
- **THEN** Log.CloseAndFlush() SHALL be called after all other shutdown tasks complete
- **AND** no log entries SHALL be lost due to buffering

### Requirement: View subscription disposal via CompositeDisposable
All View code-behind Rx subscriptions SHALL be tracked in a CompositeDisposable and disposed when the View is deactivated or closed.

#### Scenario: SkipListWindow subscription cleanup
- **WHEN** SkipListWindow is closed
- **THEN** all Rx subscriptions created in the code-behind SHALL be disposed

#### Scenario: ProgressWindow DataContext change
- **WHEN** ProgressWindow.DataContext changes to a new ViewModel
- **THEN** subscriptions to the previous ViewModel SHALL be disposed
- **AND** new subscriptions to the new ViewModel SHALL be created

### Requirement: Double-dispose guard on ProgressWindow
ProgressWindow SHALL guard against double disposal from both OnCloseRequested and OnClosed handlers.

#### Scenario: Close triggers both handlers
- **WHEN** ProgressWindow is closed (triggering both OnCloseRequested and OnClosed)
- **THEN** the disposal logic SHALL execute only once
- **AND** no ObjectDisposedException SHALL be thrown

### Requirement: ConfigurationService implements IAsyncDisposable
ConfigurationService SHALL implement IAsyncDisposable to properly flush pending saves during async disposal.

#### Scenario: Async disposal flushes saves
- **WHEN** ConfigurationService is disposed via DisposeAsync
- **THEN** pending config saves SHALL be flushed to disk
- **AND** the debounce subscription, subjects, and semaphore SHALL be disposed

### Requirement: FileSystemWatcher disposal on error paths
ConfigWatcherService SHALL dispose the FileSystemWatcher if an error occurs during StartWatching initialization.

#### Scenario: Error during watcher setup
- **WHEN** FileSystemWatcher initialization fails
- **THEN** the partially constructed watcher SHALL be disposed
- **AND** no resource leak SHALL occur
