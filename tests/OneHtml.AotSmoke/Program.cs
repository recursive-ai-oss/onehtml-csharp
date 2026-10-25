using System;

// Calls into each package so the NativeAOT publish has to trim and compile all three.
Console.WriteLine(RecursiveAI.OneHtml.PackageInfo.Name);
Console.WriteLine(RecursiveAI.OneHtml.Accessibility.PackageInfo.Name);
Console.WriteLine(RecursiveAI.OneHtml.Accessibility.Validation.PackageInfo.Name);
return 0;
