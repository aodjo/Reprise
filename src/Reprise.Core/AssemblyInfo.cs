using System.Runtime.CompilerServices;

// The lyrics service keeps its parsers and candidate scoring internal, since
// they expose the shape of third-party responses rather than anything the
// desktop layer needs. Those are exactly the pieces worth pinning with tests.
[assembly: InternalsVisibleTo("Reprise.Core.Tests")]
