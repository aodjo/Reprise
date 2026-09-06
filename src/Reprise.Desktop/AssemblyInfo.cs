using System.Runtime.CompilerServices;

// The panel's animation curves are internal: they are shape, not API, and
// nothing outside the drawing code calls them. They are also the pieces that
// have to match the macOS app frame for frame, so they are worth pinning.
[assembly: InternalsVisibleTo("Reprise.Desktop.Tests")]
