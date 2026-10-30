namespace Reprise.Desktop;

/// <summary>
/// A platform setup message shown in the player panel after startup.
/// </summary>
/// <param name="Title">Short action-oriented heading.</param>
/// <param name="Message">Instructions the user can follow when convenient.</param>
public sealed record StartupNotice(string Title, string Message);
