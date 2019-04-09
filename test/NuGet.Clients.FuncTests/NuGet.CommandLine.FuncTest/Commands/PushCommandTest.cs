// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using NuGet.CommandLine.Test;
using NuGet.Test.Utility;
using System;
using System.IO;
using System.Net;
using System.Threading;
using Xunit;

namespace NuGet.CommandLine.FuncTest.Commands
{
    public class PushCommandTest
    {
        /// <summary>
        /// 100 seconds is significant because that is the default timeout on <see cref="HttpClient"/>.
        /// Related to https://github.com/NuGet/Home/issues/2785.
        /// </summary>
        [Fact]
        public void PushCommand_AllowsTimeoutToBeSpecifiedHigherThan100Seconds()
        {
            // Arrange
            using (var packageDirectory = TestDirectory.Create())
            {
                var nuget = Util.GetNuGetExePath();
                var sourcePath = Util.CreateTestPackage("PackageA", "1.1.0", packageDirectory);
                var outputPath = Path.Combine(packageDirectory, "pushed.nupkg");

                using (var server = new MockServer())
                {
                    server.Put.Add("/push", r =>
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(101));

                        byte[] buffer = MockServer.GetPushedPackage(r);
                        using (var outputStream = new FileStream(outputPath, FileMode.Create))
                        {
                            outputStream.Write(buffer, 0, buffer.Length);
                        }

                        return HttpStatusCode.Created;
                    });

                    server.Start();

                    // Act
                    var result = CommandRunner.Run(
                        nuget,
                        packageDirectory,
                        $"push {sourcePath} -Source {server.Uri}push -Timeout 110",
                        waitForExit: true,
                        timeOutInMilliseconds: 120 * 1000); // 120 seconds

                    // Assert
                    server.Stop();
                    Assert.True(0 == result.Item1, $"{result.Item2} {result.Item3}");
                    Assert.Contains("Your package was pushed.", result.Item2);
                    Assert.True(File.Exists(outputPath), "The package should have been pushed");
                    Assert.Equal(File.ReadAllBytes(sourcePath), File.ReadAllBytes(outputPath));
                }
            }
        }

        [Fact]
        public void PushCommand_AllowsTimeoutToBeSpecifiedLowerThan100Seconds()
        {
            // Arrange
            using (var packageDirectory = TestDirectory.Create())
            {
                var nuget = Util.GetNuGetExePath();
                var sourcePath = Util.CreateTestPackage("PackageA", "1.1.0", packageDirectory);
                var outputPath = Path.Combine(packageDirectory, "pushed.nupkg");

                using (var server = new MockServer())
                {
                    server.Put.Add("/push", r =>
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(5));

                        byte[] buffer = MockServer.GetPushedPackage(r);
                        using (var outputStream = new FileStream(outputPath, FileMode.Create))
                        {
                            outputStream.Write(buffer, 0, buffer.Length);
                        }

                        return HttpStatusCode.Created;
                    });

                    server.Start();

                    // Act
                    var result = CommandRunner.Run(
                        nuget,
                        packageDirectory,
                        $"push {sourcePath} -Source {server.Uri}push -Timeout 1",
                        waitForExit: true,
                        timeOutInMilliseconds: 20 * 1000); // 20 seconds

                    // Assert
                    server.Stop();
                    Assert.True(1 == result.Item1, $"{result.Item2} {result.Item3}");
                    Assert.DoesNotContain("Your package was pushed.", result.Item2);
                    Assert.False(File.Exists(outputPath), "The package should not have been pushed");
                }
            }
        }

        [Fact]
        public void PushCommand_ContinueOnErrorNotSpecified_InvalidHaltsPush()
        {
            // Arrange
            using (var packageDirectory = TestDirectory.Create())
            {
                var nuget = Util.GetNuGetExePath();
                var sourcePath = Util.CreateTestPackage("PackageA", "1.1.0", packageDirectory);
                var outputPath = Path.Combine(packageDirectory, "pushed.nupkg");

                using (var server = new MockServer())
                {
                    SetupMockServerForContinueOnError(server, outputPath, null, true);

                    server.Start();

                    // Act
                    var result = CommandRunner.Run(
                        nuget,
                        packageDirectory,
                        $"push {sourcePath} -Source {server.Uri}push -Timeout 110",
                        waitForExit: true,
                        timeOutInMilliseconds: 120 * 1000); // 120 seconds

                    // Assert
                    server.Stop();

                    Assert.False(0 == result.Item1, result.AllOutput);
                    Assert.DoesNotContain("Your package was pushed.", result.Item2);
                    Assert.Contains("500 (Internal Server Error)", result.AllOutput);
                }
            }
        }

        [Fact]
        public void PushCommand_ContinueOnErrorNotSpecified_DuplicateHaltsPush()
        {
            // Arrange
            using (var packageDirectory = TestDirectory.Create())
            {
                var nuget = Util.GetNuGetExePath();
                var sourcePath = Util.CreateTestPackage("PackageA", "1.1.0", packageDirectory);
                var outputPath = Path.Combine(packageDirectory, "pushed.nupkg");

                var sourcePath2 = Util.CreateTestPackage("PackageB", "1.1.0", packageDirectory);
                var outputPath2 = Path.Combine(packageDirectory, "pushed2.nupkg");

                using (var server = new MockServer())
                {
                    SetupMockServerForContinueOnError(server, outputPath, outputPath2, false);

                    server.Start();

                    // Act
                    var result = CommandRunner.Run(
                        nuget,
                        packageDirectory,
                        $"push {sourcePath} -Source {server.Uri}push -Timeout 110",
                        waitForExit: true,
                        timeOutInMilliseconds: 120 * 1000); // 120 seconds

                    //Run again so that it will be a duplicate push.
                    var result2 = CommandRunner.Run(
                        nuget,
                        packageDirectory,
                        $"push {sourcePath} -Source {server.Uri}push -Timeout 110",
                        waitForExit: true,
                        timeOutInMilliseconds: 120 * 1000); // 120 seconds

                    var result3 = CommandRunner.Run(
                       nuget,
                       packageDirectory,
                       $"push {sourcePath2} -Source {server.Uri}push -Timeout 110",
                       waitForExit: true,
                       timeOutInMilliseconds: 120 * 1000); // 120 seconds

                    // Assert
                    server.Stop();
                    Assert.True(0 == result.Item1, $"{result.Item2} {result.Item3}");
                    Assert.Contains("Your package was pushed.", result.Item2);
                    Assert.True(File.Exists(outputPath), "The package should have been pushed");
                    Assert.DoesNotContain("Response status code does not indicate success", result.AllOutput);
                    Assert.DoesNotContain("Skipping existing package.", result.AllOutput);
                    Assert.Equal(File.ReadAllBytes(sourcePath), File.ReadAllBytes(outputPath));

                    // Second run of command is the duplicate.
                    Assert.False(0 == result2.Item1, result2.AllOutput);
                    Assert.Contains("Response status code does not indicate success", result2.AllOutput);
                    Assert.DoesNotContain("Skipping existing package.", result2.AllOutput);
                    Assert.Equal(File.ReadAllBytes(sourcePath), File.ReadAllBytes(outputPath));

                    //Apparently our CommandRunner does not handle batches so this doesn't work.
                    //// Third run after a duplicate should fail without the ContinueOnError flag.
                    //Assert.False(0 == result3.Item1, $"{result3.Item2} {result3.Item3}");
                    //Assert.DoesNotContain("Your package was pushed.", result3.Item2);
                    //Assert.DoesNotContain("Skipping existing package.", result2.AllOutput);
                    //Assert.Contains("Response status code does not indicate success", result.AllOutput);
                    //Assert.False(File.Exists(outputPath2), "The package should not have been pushed");

                    //Assert.Equal(File.ReadAllBytes(sourcePath2), File.ReadAllBytes(outputPath2));

                }
            }
        }

        [Fact]
        public void PushCommand_ContinueOnErrorDuplicate_DuplicateProceedsPush()
        {
            // Arrange
            using (var packageDirectory = TestDirectory.Create())
            {
                var nuget = Util.GetNuGetExePath();
                var sourcePath = Util.CreateTestPackage("PackageA", "1.1.0", packageDirectory);
                var outputPath = Path.Combine(packageDirectory, "pushed.nupkg");

                var sourcePath2 = Util.CreateTestPackage("PackageB", "1.1.0", packageDirectory);
                var outputPath2 = Path.Combine(packageDirectory, "pushed2.nupkg");

                using (var server = new MockServer())
                {
                    SetupMockServerForContinueOnError(server, outputPath, outputPath2, false);

                    server.Start();

                    // Act
                    var result = CommandRunner.Run(
                        nuget,
                        packageDirectory,
                        $"push {sourcePath} -Source {server.Uri}push -Timeout 110 -ContinueOnError duplicate",
                        waitForExit: true,
                        timeOutInMilliseconds: 120 * 1000); // 120 seconds

                    //Run again so that it will be a duplicate push but use the option to continue on errors.
                    var result2 = CommandRunner.Run(
                        nuget,
                        packageDirectory,
                        $"push {sourcePath} -Source {server.Uri}push -Timeout 110 -ContinueOnError duplicate",
                        waitForExit: true,
                        timeOutInMilliseconds: 120 * 1000); // 120 seconds

                    //Third run with a different package.
                    var result3 = CommandRunner.Run(
                        nuget,
                        packageDirectory,
                        $"push {sourcePath2} -Source {server.Uri}push -Timeout 110 -ContinueOnError duplicate",
                        waitForExit: true,
                        timeOutInMilliseconds: 120 * 1000); // 120 seconds

                    // Assert
                    server.Stop();
                    Assert.True(0 == result.Item1, $"{result.Item2} {result.Item3}");
                    Assert.Contains("Your package was pushed.", result.AllOutput);
                    Assert.True(File.Exists(outputPath), "The package should have been pushed");
                    Assert.DoesNotContain("Response status code does not indicate success", result.AllOutput);
                    Assert.Equal(File.ReadAllBytes(sourcePath), File.ReadAllBytes(outputPath));

                    // Second run of command is the duplicate.
                    Assert.True(0 == result2.Item1, result2.AllOutput);
                    Assert.DoesNotContain("Your package was pushed.", result2.AllOutput);
                    Assert.Contains("Skipping existing package.", result2.AllOutput);
                    Assert.DoesNotContain("Response status code does not indicate success", result2.AllOutput);

                    // Third run after a duplicate should be successful with the ContinueOnError flag.
                    Assert.True(0 == result3.Item1, $"{result3.Item2} {result3.Item3}");
                    Assert.Contains("Your package was pushed.", result3.AllOutput);
                    Assert.True(File.Exists(outputPath2), "The package should have been pushed");
                    
                    Assert.Equal(File.ReadAllBytes(sourcePath2), File.ReadAllBytes(outputPath2));

                }
            }
        }

        [Fact]
        public void PushCommand_ContinueOnErrorInvalid_InvalidProceedsPush()
        {
            
        }


        /// <summary>
        /// Sets up the server for the steps of running 3 Push commands. First is the initial push, followed by a duplicate push, followed by a new package push.
        /// Depending on the options of the push, the duplicate will either be a warning or an error and permit or prevent the third push.
        /// </summary>
        /// <param name="server">Server object to modify.</param>
        /// <param name="outputPath">Required path to output package.</param>
        /// <param name="outputPath2">If provided, used for run 3 and after.</param>
        /// <param name="alwaysInvalidResponse">All responses are 500 HTTP errors.</param>
        private static void SetupMockServerForContinueOnError(MockServer server, string outputPath, string outputPath2, bool alwaysInvalidResponse = false)
        {
            int packageCounter = 0;
            server.Put.Add("/push", r =>
            {
                packageCounter++; //Just assume the package name is the same as before.
                byte[] buffer = MockServer.GetPushedPackage(r);

                //Switch to the second output .nupkg name after first run.
                var outputPathBasedOnCount = outputPath2 == null || packageCounter < 3 ? outputPath : outputPath2;

                using (var outputStream = new FileStream(outputPathBasedOnCount, FileMode.Create))
                {
                    outputStream.Write(buffer, 0, buffer.Length);
                }

                if (alwaysInvalidResponse)
                {
                    //Always return a response indicating an Invalid package.
                    return HttpStatusCode.InternalServerError;
                }
                else
                {
                    //Second run will be treated as duplicate.
                    if (packageCounter == 2)
                    {
                        return HttpStatusCode.Conflict;
                    }
                    else
                    {
                        return HttpStatusCode.Created;
                    }
                }
            });
        }


    }
}
