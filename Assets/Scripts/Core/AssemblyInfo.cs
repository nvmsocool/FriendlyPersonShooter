using System.Runtime.CompilerServices;

// Lets the engine-free unit tests (Tests/Core.Tests) check internal helpers.
[assembly: InternalsVisibleTo("Core.Tests")]
