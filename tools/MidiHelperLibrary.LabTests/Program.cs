#if TEST_LAB_OFFLINE_WINDOWS
if (args.Length != 2) throw new ArgumentException("The offline profile accepts only Functional test execution.");
#endif
return PortableXunit.Run(args, ["MightBeUseful.Midi.Tests"]);
