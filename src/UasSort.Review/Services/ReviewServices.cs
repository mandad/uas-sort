// src/UasSort.Review/Services/ReviewServices.cs
namespace UasSort.Review;

/// <summary>Log sink for things the VMs report but never throw (unknown map messages, ignored plans), and facts worth keeping for a
/// later "why" (Info: why [Clean up card…] is unavailable, Task U4).</summary>
public interface IReviewLog
{
    void Warn(string message);
    void Info(string message);
}

/// <summary>Free-space readout for the footer, Setup and Settings (Part 11 binds it to IFileOps.FreeBytes).</summary>
public interface IFreeSpace
{
    long? FreeBytes(string anyPathOnVolume);
}

/// <summary>Everything the Review stage needs from the outside (Ref §4.2 "Services").</summary>
public sealed record ReviewServices(IUiDispatcher Ui, IDialogService Dialogs, IShellLauncher Shell, IThumbnailSource Thumbs,
                                    IDraftStore Drafts, IFreeSpace Space, TimeProvider Time, IReviewLog Log);
