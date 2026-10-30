using System.Runtime.CompilerServices;

// Native API boundaries remain internal while integration tests exercise routing and update safety.
[assembly: InternalsVisibleTo("Reprise.Platform.Windows.Tests")]
