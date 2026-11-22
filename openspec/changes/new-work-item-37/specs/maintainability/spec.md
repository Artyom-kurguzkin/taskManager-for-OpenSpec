## ADDED Requirements

### Requirement: Every run writes a log
Each run of the app SHALL write its own log file `run-<yyyyMMdd-HHmmss>-<pid>.log` under the app's logs folder (`%LOCALAPPDATA%/RepoTaskManager/logs`), starting with a header line (version, pid, start time, OS), and SHALL keep only the newest 20 run logs. When a console is attached (the app started from `dotnet run` or a terminal) the same lines SHALL also go to the console. Debug lines (scans and other detail) SHALL be written only when the `RTM_LOG` environment variable asks for them.

#### Scenario: The app starts
- **WHEN** the app starts
- **THEN** a new run log exists with the header line, and at most 20 run logs remain in the folder

#### Scenario: More detail is asked for
- **WHEN** the app starts with `RTM_LOG=Debug`
- **THEN** the run log also holds the Debug lines (file-watcher scans)

### Requirement: Traces follow a command end to end
Every UI command, MCP tool call, queued write and file-watcher scan SHALL run inside an activity, and every log line SHALL carry the trace id of the activity it ran under, so one id follows a user's action from the UI through the write to the scan that picks it up. Start and end lines SHALL carry the duration; a refusal SHALL be logged as a warning and an exception as an error with its type, message and stack.

#### Scenario: A UI command writes and the watcher scans
- **WHEN** the user changes an item's status
- **THEN** the command, the queued write and the scan log under one trace id, each with its duration

#### Scenario: An agent calls a tool
- **WHEN** an MCP tool is called
- **THEN** a line names the tool, its duration and its trace id

### Requirement: Logs show in the terminal the app was started from
When the app is started from a terminal (`dotnet run`, or the exe typed in a console) it SHALL attach to that terminal and write its log lines there as well as to the run log, keeping a redirected standard output and opening a console of its own only with `--console`. Started from a shortcut or Explorer it SHALL open no console window, and Photino's own message dump SHALL be off.

#### Scenario: Started from a terminal
- **WHEN** the app is started from a terminal
- **THEN** the terminal shows the header line and every log line of the run as they are written

### Requirement: The current log opens from the side menu
The side menu SHALL offer "Open current log", which opens this run's log file in the default program for `.log`, or in Notepad on Windows when `.log` has no default program.

#### Scenario: No program opens .log
- **WHEN** the user picks Open current log on Windows and no program is set for `.log`
- **THEN** the run log opens in Notepad instead of the Open with picker

### Requirement: Every user action is logged
Every command the user gives on a page (a status change, a subtask ticked, a setting changed, an item hidden or created, a note saved) SHALL write one Information line in the `ui` category naming the page, the action and the item, under the trace id of the command. A refused command SHALL write a Warning instead of a `ui` line.

#### Scenario: A status change and a subtask tick
- **WHEN** the user changes an item's status and ticks one of its subtasks
- **THEN** the run log holds one `ui` line for each, naming the page, the action and the item

### Requirement: Every failure leaves a trace
The app SHALL log, with its type, message and stack, every failure it can see: an unhandled exception, an unobserved task exception, an exception the window's dispatcher caught, a component that throws inside the app's error boundary, a script error on the page (at most 20 a minute, the rest counted), an exception on the file watcher's timer, and the WebView2 browser process ending with a failure code.

#### Scenario: An unhandled exception
- **WHEN** an exception reaches no handler
- **THEN** it is logged first, with its stack and whether the process ends

#### Scenario: A script error on the page
- **WHEN** a script on the page throws an uncaught error
- **THEN** the run log holds one Warning with the error's message, source, position and stack

#### Scenario: A component fails
- **WHEN** a component inside the error boundary throws while rendering
- **THEN** the run log holds one Error with the exception type and the page, and the page shows a message with a Reload link

#### Scenario: The file watcher's timer throws
- **WHEN** a listener throws on the watcher's timer
- **THEN** the exception is logged and the timer keeps working

#### Scenario: The browser process fails
- **WHEN** the WebView2 browser process ends with a failure code
- **THEN** an Error names its pid and exit code

### Requirement: A freeze leaves a stack
A watchdog SHALL ping the window's UI thread; when the window has not answered for 30 seconds it SHALL log one Error and write a dump of the process (every thread's stack) to the logs folder, naming the dump's path in the record (or saying that the dump could not be written). When the window answers again it SHALL log that and re-arm.

#### Scenario: The UI thread is blocked
- **WHEN** the window does not answer its ping for 30 seconds
- **THEN** exactly one freeze record is logged with the path of a dump of every thread's stack, and a line follows when the window answers again

### Requirement: An unclean exit is named, not guessed
At start the app SHALL report each earlier run that did not exit cleanly as a Warning that names the cause it knows: stopped by a publish (the dist publish leaves a stop file for each process it stops; read once, then removed), ended by a Windows shutdown or sign-out, or otherwise "killed or crashed", with where Windows keeps crash dumps. A run that exited cleanly or is still running SHALL not be reported.

#### Scenario: A killed run
- **WHEN** the app is killed and started again
- **THEN** the new run log holds a Warning that the earlier run did not exit cleanly: killed or crashed

#### Scenario: A publish stopped the run
- **WHEN** the dist publish stopped the running app and the app starts again
- **THEN** the Warning says the run was stopped by a publish, and says it only once
