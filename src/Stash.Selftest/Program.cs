// Runs the core self-tests on any OS (the app's own "stash selftest" runs the same suites on Windows).
using Stash.Core;
return SelfTest.Run(Console.Out, args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null, args.Contains("--list")) == 0 ? 0 : 1;
