using System;
using System.IO;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Core.Apps;

/// <summary>
/// An entry of Windows' installed-apps list (<c>shell:AppsFolder</c>, what Start shows under
/// All apps), with the two rules ADR-009 settles: whether the picker lists it, and what it is
/// stored as once picked.
/// </summary>
/// <param name="Name">The name Start shows.</param>
/// <param name="Aumid">Its AppUserModelID, the entry's parsing name in the folder.</param>
/// <param name="Target">What a Win32 entry starts — a file path or a web address; <see langword="null"/> for a packaged app.</param>
/// <param name="Arguments">The arguments Start passes to <paramref name="Target"/>, if any.</param>
/// <param name="PackageFullName">A packaged app's package, version included.</param>
public sealed record InstalledApp(string Name, string Aumid, string? Target, string? Arguments, string? PackageFullName)
{
    // Help files, web links and documents: entries that open something rather than start an app.
    private static readonly string[] _documents = [".url", ".chm", ".htm", ".html", ".pdf", ".txt", ".rtf", ".md", ".xml", ".doc", ".docx"];

    /// <summary>
    /// Whether the picker shows this entry: a packaged app, or a Win32 entry that starts a
    /// program or a console — not a web address and not a document.
    /// </summary>
    public bool IsListed
    {
        get
        {
            if (Target is null)
            {
                return true;
            }

            if (Target.Contains("://", StringComparison.Ordinal))
            {
                return false;
            }

            var extension = Path.GetExtension(Target);
            return !Array.Exists(_documents, d => string.Equals(d, extension, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// The entry to store (ADR-009, rule C): a plain program — an <c>.exe</c> or <c>.lnk</c> target
    /// started without arguments — by its path, as if it had been browsed for; anything else by its
    /// <c>shell:AppsFolder</c> entry, so it starts the way Start starts it. Either way it is named as
    /// Start names it.
    /// </summary>
    /// <returns>The new entry.</returns>
    public AppEntry ToEntry()
    {
        if (string.IsNullOrWhiteSpace(Arguments) && AppEntry.TryCreateFromPath(Target, out var program))
        {
            program.Name = Name;
            return program;
        }

        return AppEntry.TryCreateFromAumid(Name, Aumid, out var app)
            ? app
            : throw new InvalidOperationException($"An installed app without a name or id: '{Name}', '{Aumid}'.");
    }
}
