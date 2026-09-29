using UasSort.Platform.Io;

namespace UasSort.Platform.Card;

public sealed class WindowsCardReaderFactory(Settings settings, string appDataDir, string machine, IPathFacts facts,
                                             IDirectoryLister lister) : ICardReaderFactory
{
    public ICardReader Open(CardSource source, CardIdentity identity)
        => new WindowsCardReader(source, identity, GuardContexts.For(settings, appDataDir, machine, facts, source.Root), lister);
}
