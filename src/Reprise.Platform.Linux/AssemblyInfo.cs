using System.Runtime.CompilerServices;

// Opens the MPRIS internals to the Linux test assembly. The IMprisBus seam and
// MprisPropertyMapper are internal because they expose Tmds.DBus types that are
// not part of the public surface, but they are also where the parsing and
// command-routing logic worth testing lives. Granting the test assembly access
// keeps them testable without widening the API.
[assembly: InternalsVisibleTo("Reprise.Platform.Linux.Tests")]
