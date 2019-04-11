// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NuGet.Commands;

namespace NuGet.CommandLine
{
    [Command(typeof(NuGetCommand), "push", "PushCommandDescription;DefaultConfigDescription",
        MinArgs = 1, MaxArgs = 2, UsageDescriptionResourceName = "PushCommandUsageDescription",
        UsageSummaryResourceName = "PushCommandUsageSummary", UsageExampleResourceName = "PushCommandUsageExamples")]
    public class PushCommand : Command
    {
        [Option(typeof(NuGetCommand), "PushCommandSourceDescription", AltName = "src")]
        public string Source { get; set; }

        [Option(typeof(NuGetCommand), "CommandApiKey")]
        public string ApiKey { get; set; }

        [Option(typeof(NuGetCommand), "PushCommandSymbolSourceDescription")]
        public string SymbolSource { get; set; }

        [Option(typeof(NuGetCommand), "SymbolApiKey")]
        public string SymbolApiKey { get; set; }

        [Option(typeof(NuGetCommand), "PushCommandTimeoutDescription")]
        public int Timeout { get; set; }

        [Option(typeof(NuGetCommand), "PushCommandDisableBufferingDescription")]
        public bool DisableBuffering { get; set; }

        [Option(typeof(NuGetCommand), "PushCommandNoSymbolsDescription")]
        public bool NoSymbols { get; set; }

        [Option(typeof(NuGetCommand), "CommandNoServiceEndpointDescription")]
        public bool NoServiceEndpoint { get; set; }

        [Option(typeof(NuGetCommand), "PushCommandContinueOnErrorDescription")]
        public ICollection<string> ContinueOnError { get; } = new List<string>();

        public override async Task ExecuteCommandAsync()
        {
            string packagePath = Arguments[0];
            string apiKeyValue = null;

            if (!string.IsNullOrEmpty(ApiKey))
            {
                apiKeyValue = ApiKey;
            }
            else if (Arguments.Count > 1 && !string.IsNullOrEmpty(Arguments[1]))
            {
                apiKeyValue = Arguments[1];
            }

            var continueOnDuplicate =  ContinueOnError.Contains(CommandLineConstants.ContinueOnErrorOptions.duplicate.ToString(), StringComparer.CurrentCultureIgnoreCase);
            var continueOnInvalid = ContinueOnError.Contains(CommandLineConstants.ContinueOnErrorOptions.invalid.ToString(), StringComparer.CurrentCultureIgnoreCase);
            //Check that the provided option(s) are one of the ContinueOnErrorOptions.
            //Note: Removing "," is done because the Enum Parse by default will accept a comma-delimited list and say that it's valid.
            //      Our delimiter is ";" (eg, "{validOption1};{validOption2}" should succeed)
            //      but I want an error if a comma-delimited list of valid options is provided (eg, "{validOption1},{validOption2}" should error).
            var continueOnErrorUnknown = ContinueOnError.FirstOrDefault(coe =>
                                                        !Enum.TryParse<CommandLineConstants.ContinueOnErrorOptions>(coe.Replace(",",string.Empty).ToLower()
                                                        ,out var result));
            if (string.IsNullOrWhiteSpace(continueOnErrorUnknown) == false)
            {
                throw new ArgumentException($"Invalid option {continueOnErrorUnknown}", nameof(ContinueOnError));
            }
            try
            {
                await PushRunner.Run(
                    Settings,
                    SourceProvider,
                    packagePath,
                    Source,
                    apiKeyValue,
                    SymbolSource,
                    SymbolApiKey,
                    Timeout,
                    DisableBuffering,
                    NoSymbols,
                    NoServiceEndpoint,
                    continueOnDuplicate,
                    continueOnInvalid,
                    Console);
            }
            catch (TaskCanceledException ex)
            {
                string timeoutMessage = LocalizedResourceManager.GetString(nameof(NuGetResources.PushCommandTimeoutError));
                throw new AggregateException(ex, new Exception(timeoutMessage));
            }
            catch (Exception ex)
            {
                if (ex is HttpRequestException && ex.InnerException is WebException)
                {
                    throw ex.InnerException;
                }

                throw;
            }
        }
    }
}
