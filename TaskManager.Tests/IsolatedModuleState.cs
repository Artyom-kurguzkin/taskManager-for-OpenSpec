using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("TaskManager.Tests.IsolatingTestFramework", "TaskManager.Tests")]

namespace TaskManager.Tests;

/// <summary>
/// Gives every test its own module state. The modules keep their state in statics (the app has one of each); each module also has an
/// AsyncLocal seam, <c>Isolated</c>, that replaces that state for the code running from where it is set. Setting it once per test case,
/// before the test class is constructed, means everything the test runs (its constructor, its awaits, the tasks and timers it starts)
/// sees the test's own open repositories, caches (the host's, in AppState/), swapped steps and counters, and tests in different classes can run at the same time.
/// </summary>
internal static class IsolatedModuleState
{
    internal static void Begin()
    {
        TaskManager.AppState.Caches.Install();   // once per process: the modules answer from the caches below
        TaskManager.AppState.Caches.Isolated.Value = new();
        TaskManager.AppState.Caches.S.Git.Background = work => { work(); return Task.CompletedTask; };   // git's answer about the work tree arrives on the thread pool in the app; a test gets it at once, and the ones that cover the hand-over queue it themselves
        OpenSpecStore.OpenSpecStoreModule.Isolated.Value = new();
        WorkItems.WorkItemsModule.Isolated.Value = new();
        Notes.NotesModule.Isolated.Value = new();
        GitHistory.GitHistoryModule.Isolated.Value = new() { RealWatchers = false };   // no test starts a file system watcher on .git; the ones that cover it drive a watcher themselves
        TaskManager.Components.RenderCounts.Isolated.Value = new();
    }
}

/// <summary>xunit's own framework, with one change: each test case starts in fresh module state (<see cref="IsolatedModuleState"/>).</summary>
public sealed class IsolatingTestFramework(IMessageSink diagnosticMessageSink) : XunitTestFramework(diagnosticMessageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Executor(AssemblyName assemblyName, ISourceInformationProvider sourceInformationProvider, IMessageSink diagnosticMessageSink)
        : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
    {
        protected override async void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        {
            using AssemblyRunner runner = new(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
            await runner.RunAsync();
        }
    }

    private sealed class AssemblyRunner(ITestAssembly testAssembly, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
        : XunitTestAssemblyRunner(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
    {
        protected override Task<RunSummary> RunTestCollectionAsync(IMessageBus messageBus, ITestCollection testCollection, IEnumerable<IXunitTestCase> testCases, CancellationTokenSource cancellationTokenSource) =>
            new CollectionRunner(testCollection, testCases, DiagnosticMessageSink, messageBus, TestCaseOrderer, new ExceptionAggregator(Aggregator), cancellationTokenSource).RunAsync();
    }

    private sealed class CollectionRunner(ITestCollection testCollection, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageBus messageBus, ITestCaseOrderer testCaseOrderer, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
        : XunitTestCollectionRunner(testCollection, testCases, diagnosticMessageSink, messageBus, testCaseOrderer, aggregator, cancellationTokenSource)
    {
        protected override Task<RunSummary> RunTestClassAsync(ITestClass testClass, IReflectionTypeInfo @class, IEnumerable<IXunitTestCase> testCases) =>
            new ClassRunner(testClass, @class, testCases, DiagnosticMessageSink, MessageBus, TestCaseOrderer, new ExceptionAggregator(Aggregator), CancellationTokenSource, CollectionFixtureMappings).RunAsync();
    }

    private sealed class ClassRunner(ITestClass testClass, IReflectionTypeInfo @class, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageBus messageBus, ITestCaseOrderer testCaseOrderer, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource, IDictionary<Type, object> collectionFixtureMappings)
        : XunitTestClassRunner(testClass, @class, testCases, diagnosticMessageSink, messageBus, testCaseOrderer, aggregator, cancellationTokenSource, collectionFixtureMappings)
    {
        protected override Task<RunSummary> RunTestMethodAsync(ITestMethod testMethod, IReflectionMethodInfo method, IEnumerable<IXunitTestCase> testCases, object[] constructorArguments) =>
            new MethodRunner(testMethod, Class, method, testCases, DiagnosticMessageSink, MessageBus, new ExceptionAggregator(Aggregator), CancellationTokenSource, constructorArguments).RunAsync();
    }

    private sealed class MethodRunner(ITestMethod testMethod, IReflectionTypeInfo @class, IReflectionMethodInfo method, IEnumerable<IXunitTestCase> testCases, IMessageSink diagnosticMessageSink, IMessageBus messageBus, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource, object[] constructorArguments)
        : XunitTestMethodRunner(testMethod, @class, method, testCases, diagnosticMessageSink, messageBus, aggregator, cancellationTokenSource, constructorArguments)
    {
        protected override async Task<RunSummary> RunTestCaseAsync(IXunitTestCase testCase)
        {
            IsolatedModuleState.Begin();   // set here, in this async method, it flows into the test case and nowhere else
            return await base.RunTestCaseAsync(testCase);
        }
    }
}
