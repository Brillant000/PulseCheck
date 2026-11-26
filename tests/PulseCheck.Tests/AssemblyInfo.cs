using Xunit;

// Several tests change global state (the current language in Loc, the registry prefix in Reg).
// Running test classes in parallel made them overwrite each other at random, so they run one after the other.
// The whole suite takes well under a second, so nothing is lost.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
