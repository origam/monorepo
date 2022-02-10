﻿#region license
/*
Copyright 2005 - 2021 Advantage Solutions, s. r. o.

This file is part of ORIGAM (http://www.origam.org).

ORIGAM is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

ORIGAM is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with ORIGAM. If not, see <http://www.gnu.org/licenses/>.
*/
#endregion

//using ConfigurationManager = Origam.ConfigurationManager;

using CommandLine;
using CommandLine.Text;
using Origam.DA;
using Origam.DA.Service;
using Origam.OrigamEngine;
using Origam.Schema;
using Origam.Schema.MenuModel;
using Origam.Workbench.Services;
using System;
using System.Collections;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using BrockAllen.IdentityReboot;

namespace Origam.Utils
{
    class Program
    {

        private static QueueProcessor queueProcessor;

        private delegate bool EventHandler(CtrlType sig);

        private static EventHandler cancelHandler;

        private static log4net.ILog log =
            log4net.LogManager.GetLogger(System.Reflection.MethodBase
                .GetCurrentMethod().DeclaringType);

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler,
            bool add);

        private enum CtrlType
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        class CreateHashIndexOptions
        {
            [Option('i', "input", Required = true,
                HelpText = "Folder for which the index will be created.")]
            public string Input { get; set; }

            [Option('m', "mask", Required = true,
                HelpText = "Search pattern.")]
            public string Mask { get; set; }

            [Option('o', "output", Required = true,
                HelpText = "Path/Name of file where the index will be stored.")]
            public string Output { get; set; }
        }

        class RunUpdateScriptsOptions
        {
        }

        class RestarServerOptions
        {
        }

        class CompareSchemaOptions
        {
            [Option('d', "missing-in-db", DefaultValue = false,
                HelpText = "Display elements missing in database.")]
            public bool MissingInDB { get; set; }

            [Option('s', "missing-in-schema", DefaultValue = false,
                HelpText = "Display elements missing in schema.")]
            public bool MissingInSchema { get; set; }

            [Option('x', "existing-but-different", DefaultValue = false,
                HelpText = "Display elements, that exist but are different.")]
            public bool ExistingButDifferent { get; set; }
        }

        private class ProcessQueueOptions
        {
            [Option('c', "queueCode", Required = true,
                HelpText = "Reference code of the queue to process.")]
            public string QueueRefCode { get; set; }

            [Option('p', "parallelism", Required = true,
                HelpText = "MaxDegreeOfParallelism.")]
            public int Parallelism { get; set; }

            [Option('w', "forceWait_ms", Required = false, DefaultValue = 0,
                HelpText =
                    "Delay between processing of queue items in miliseconds.")]
            public int ForceWait_ms { get; set; }

            [Option('v', "verbose", DefaultValue = true,
                HelpText = "Prints all messages to standard output.")]
            public bool Verbose { get; set; }

            [ParserState] public IParserState LastParserState { get; set; }
        }

        public class ProcessCheckRules
        {
        }
        
        public class DBTestArguments
        {
            [Option('t', "tries", Required = true,
                HelpText = "How many times to run test.")]
            public int tries { get; set; }
            [Option('d', "delay", Required = true,
                HelpText = "How long to wait till next try.")]
            public int delay { get; set; }
            [Option('c', "sql-command", Required = true,
                HelpText = "What sql-command to run.")]
            public string sqlCommand { get; set; }
        }

        public class ProcessDocGeneratorArgs
        {
            [Option('o', "output", Required = true,
                HelpText = "Output directory")]
            public string Dataout { get; set; }

            [Option('l', "language", Required = true,
                HelpText = "Localization(ie. cs-CZ).")]
            public string Language { get; set; }

            [Option('x', "xslt", Required = true, HelpText = "Xslt template")]
            public string Xslt { get; set; }

            [Option('r', "rootfilename", Required = true,
                HelpText = "Output File")]
            public string RootFile { get; set; }

            [ParserState] public IParserState LastParserState { get; set; }
        }

        internal class GeneratePassHashOptions
        {
            [Option('p', "password", Required = true,
                HelpText = "String to hash")]
            public string Password { get; set; }
        }

        class Options
        {
            [VerbOption("process-checkrules",
                HelpText = "Check rules in project.")]
            public ProcessCheckRules ProcessCheckRules { get; set; }

            [VerbOption("process-docgenerator",
                HelpText = "Generate Menu into output with xslt template.")]
            public ProcessDocGeneratorArgs ProcessDocGeneratorArgs { get; set; }

            [VerbOption("generate-password-hash",
                HelpText =
                    "Generate hash of supplied password. The hash can be inserted into column Password in OrigamUser table as development password reset.")]
            public GeneratePassHashOptions GeneratePassHashOptions { get; set; }
            
            [VerbOption("test-db", HelpText = "Try to connect to database and run a sql command.")]
            public DBTestArguments DbTestArguments { get; set; }
#if !NETCORE2_1
            [VerbOption("process-queue",
                HelpText = "Process a queue.")]
            public ProcessQueueOptions ProcessQueue { get; set; }

            [VerbOption("run-scripts",
                HelpText = "Runs update scripts.")]
            public RunUpdateScriptsOptions RunUpdateScriptsVerb { get; set; }

            [VerbOption("restart-server", HelpText = "Invokes server restart.")]
            public RestarServerOptions RestartServerVerb { get; set; }

            [VerbOption("create-hash-index",
                HelpText =
                    "Creates hash index file on the contents of the given folder.")]
            public CreateHashIndexOptions CreateHashIndexVerb { get; set; }

            [VerbOption("compare-schema",
                HelpText =
                    "Compares schema with database. If no comparison switches are defined, no comparison is done. More than one switch can be enabled.")]
            public CompareSchemaOptions CompareSchemaVerb { get; set; }
#endif
            [ParserState] 
            public IParserState LastParserState { get; set; }

            [HelpVerbOption]
            public string GetUsage(string verb)
            {
                return HelpText.AutoBuild(this, verb);
            }
        }


        private static bool CancelHandler(CtrlType sig)
        {
            log.Info("Exiting system due to external CTRL-C," +
                     " or process kill, or shutdown, please wait...");
            queueProcessor.Cancel();
            log.Info("Cleanup complete");
            Environment.Exit(-1);
            return true;
        }

        static int Main(string[] args)
        {
            string invokedVerb = "";
            object invokedVerbInstance = null;
            var options = new Options();
            if (!Parser.Default.ParseArguments(args, options,
                (verb, subOptions) =>
                {
                    invokedVerb = verb;
                    invokedVerbInstance = subOptions;
                }))
            {
                return 1;
            }

            switch (invokedVerb)
            {
                case "process-checkrules":
                {
                        EntryAssembly();
                        return ProcesRule(
                        (ProcessCheckRules)invokedVerbInstance);
                }
                case "process-docgenerator":
                {
                        EntryAssembly();
                        return ProcesDocGenerator(
                        (ProcessDocGeneratorArgs)invokedVerbInstance);
                }
                case "generate-password-hash":
                {
                        EntryAssembly();
                        return HashPassword(
                        (GeneratePassHashOptions)invokedVerbInstance);
                }
#if !NETCORE2_1
                case "process-queue":
                {
                        EntryAssembly();
                        return ProcesQueue(
                        (ProcessQueueOptions)invokedVerbInstance);
                }
                case "run-scripts":
                {
                        EntryAssembly();
                        return RunUpdateScripts();
                }
                case "restart-server":
                {
                        EntryAssembly();
                        return RestartServer();
                }
                case "create-hash-index":
                {
                        EntryAssembly();
                        return CreateHashIndex(
                        (CreateHashIndexOptions)invokedVerbInstance);
                }
                case "compare-schema":
                {
                        EntryAssembly();
                        return CompareSchema(
                        (CompareSchemaOptions)invokedVerbInstance);
                }
                case "test-db":
                {
                    return TestDatabase(
                        (DBTestArguments)invokedVerbInstance);
                }
#endif
                default:
                {
                        EntryAssembly();
                        return 1;
                }
            }
        }

        private static void EntryAssembly()
        {
            Console.WriteLine(string.Format(Strings.ShortGnu,
                    System.Reflection.Assembly.GetEntryAssembly().GetName().Name));
        }

        private static int HashPassword(GeneratePassHashOptions options)
        {
            string hash =
                new AdaptivePasswordHasher().HashPassword(options.Password);

            log.Info("");
            log.Info("Password: " + options.Password);
            log.Info("Hash: " + hash);

            return 0;
        }

        private static int ProcesDocGenerator(ProcessDocGeneratorArgs config)
        {
            Thread.CurrentThread.CurrentUICulture =
                new CultureInfo(config.Language);
            RuntimeServiceFactoryProcessor RuntimeServiceFactory =
                new RuntimeServiceFactoryProcessor();
            OrigamEngine.OrigamEngine.ConnectRuntime(
                customServiceFactory: RuntimeServiceFactory);
            OrigamSettings settings =
                ConfigurationManager.GetActiveConfiguration();
            FilePersistenceService persistenceService =
                ServiceManager.Services.GetService(
                    typeof(FilePersistenceService)) as FilePersistenceService;
            MenuSchemaItemProvider menuprovider = new MenuSchemaItemProvider
            {
                PersistenceProvider =
                    (FilePersistenceProvider)persistenceService.SchemaProvider
            };

            FilePersistenceProvider persprovider =
                (FilePersistenceProvider)persistenceService.SchemaProvider;
            persistenceService.LoadSchema(settings.DefaultSchemaExtensionId,
                false, false, "");

            var documentation = new FileStorageDocumentationService(
                persprovider,
                persistenceService.FileEventQueue);
            new DocProcessor(config.Dataout, config.Xslt, config.RootFile,
                documentation,
                menuprovider, persistenceService, null).Run();
            return 0;

        }

        private static int ProcesRule(ProcessCheckRules invokedVerbInstance)
        {
            RulesProcessor rulesProcessor = new RulesProcessor();
            return rulesProcessor.Run();
        }

        private static int ProcesQueue(ProcessQueueOptions options)
        {
            log.Info("------------ Input -------------");
            log.Info($"queueCode: {options.QueueRefCode}");
            log.Info($"parallelism: {options.Parallelism}");
            log.Info($"forceWait_ms: {options.ForceWait_ms}");
            log.Info($"-------------------------------");
            cancelHandler += CancelHandler;
            SetConsoleCtrlHandler(cancelHandler, true);
            RunQueueProcessor(options);
            log.Info("Exiting...");
            return 0;
        }

        private static void RunQueueProcessor(ProcessQueueOptions options)
        {
            try
            {
                log.Info(options);
                queueProcessor = new QueueProcessor(
                    options.QueueRefCode,
                    options.Parallelism,
                    options.ForceWait_ms
                );
                queueProcessor.Run();
            }
            catch (Exception ex)
            {
                log.Error(ex.Message);
            }
        }

        private static int RunUpdateScripts()
        {
            if (log.IsInfoEnabled)
            {
                log.Info("Running update scripts...");
            }

            OrigamEngine.OrigamEngine.ConnectRuntime(
                runRestartTimer: false, loadDeploymentScripts: true);
            IDeploymentService deployment = ServiceManager.Services.GetService(
                typeof(IDeploymentService)) as IDeploymentService;
            deployment.Deploy();
            return 0;
        }

        private static int RestartServer()
        {
            OrigamEngine.OrigamEngine.ConnectRuntime(runRestartTimer: false);
            RestartServerInternal();
            return 0;
        }

        private static void RestartServerInternal()
        {
            if (log.IsInfoEnabled)
            {
                log.Info("Invoking server restart...");
            }

            OrigamEngine.OrigamEngine.SetRestart();
        }

        private static int CreateHashIndex(CreateHashIndexOptions options)
        {
            if (log.IsInfoEnabled)
            {
                log.InfoFormat(
                    "Creating hash index file {1} on folder {0} with pattern {2}.",
                    options.Input, options.Output, options.Mask);
            }

            string[] fileNames =
                Directory.GetFiles(options.Input, options.Mask);
            HashIndexFile hashIndexFile
                = new HashIndexFile(options.Output);
            foreach (string filename in fileNames)
            {
                if (log.IsInfoEnabled)
                {
                    log.InfoFormat("Hashing {0}...", filename);
                }

                hashIndexFile.AddEntryToIndexFile(
                    hashIndexFile.CreateIndexFileEntry(
                        filename));
            }

            if (log.IsInfoEnabled)
            {
                log.Info("Hash index file finished.");
            }

            hashIndexFile.Dispose();
            return 0;
        }

        private static int CompareSchema(CompareSchemaOptions options)
        {
            if (!options.MissingInDB 
                && !options.MissingInSchema
                && !options.ExistingButDifferent)
            {
                if (log.IsInfoEnabled)
                {
                    log.Info("No comparison switches enabled...");
                }

                return 0;
            }

            OrigamEngine.OrigamEngine.ConnectRuntime(runRestartTimer: false);
            if (log.IsInfoEnabled)
            {
                log.Info(
                    $@"Comparing schema with database: missing in database({
                        options.MissingInDB}), missing in schema({
                            options.MissingInSchema}), existing but different({
                                options.ExistingButDifferent})...");
            }

            IPersistenceService persistenceService
                = ServiceManager.Services.GetService(
                    typeof(IPersistenceService)) as IPersistenceService;
            OrigamSettings settings
                = ConfigurationManager.GetActiveConfiguration() as
                    OrigamSettings;
            MsSqlDataService dataService = new MsSqlDataService(
                settings.DataConnectionString,
                settings.DataBulkInsertThreshold,
                settings.DataUpdateBatchSize);
            dataService.PersistenceProvider = persistenceService.SchemaProvider;
            ArrayList results = dataService.CompareSchema(
                persistenceService.SchemaProvider);
            if (results.Count == 0)
            {
                if (log.IsInfoEnabled)
                {
                    log.Info("No differences found.");
                }

                return 0;
            }
            else
            {
                return DisplaySchemaComparisonResults(options, results);
            }
        }

        private static int DisplaySchemaComparisonResults(
            CompareSchemaOptions options, ArrayList results)
        {
            ArrayList existingButDifferent = new ArrayList();
            ArrayList missingInDatabase = new ArrayList();
            ArrayList missingInSchema = new ArrayList();
            foreach (SchemaDbCompareResult result in results)
            {
                switch (result.ResultType)
                {
                    case DbCompareResultType.ExistingButDifferent:
                    {
                        existingButDifferent.Add(result);
                        break;
                    }
                    case DbCompareResultType.MissingInDatabase:
                    {
                        missingInDatabase.Add(result);
                        break;
                    }
                    case DbCompareResultType.MissingInSchema:
                    {
                        missingInSchema.Add(result);
                        break;
                    }
                }
            }

            int displayedResultsCount = 0;
            if (options.MissingInDB)
            {
                DisplayComparisonResultGroup(
                    missingInDatabase, "Missing in Database:");
                displayedResultsCount += missingInDatabase.Count;
            }

            if (options.MissingInSchema)
            {
                DisplayComparisonResultGroup(
                    missingInSchema, "Missing in Schema:");
                displayedResultsCount += missingInSchema.Count;
            }

            if (options.ExistingButDifferent)
            {
                DisplayComparisonResultGroup(
                    existingButDifferent, "Existing But Different:");
                displayedResultsCount += existingButDifferent.Count;
            }

            if (displayedResultsCount == 0)
            {
                if (log.IsInfoEnabled)
                {
                    log.Info("No differences found.");
                }

                return 0;
            }
            else
            {
                return 1;
            }
        }

        private static void DisplayComparisonResultGroup(
            ArrayList results, string header)
        {
            if ((results.Count > 0) && log.IsInfoEnabled)
            {
                log.Info(header);
                foreach (SchemaDbCompareResult result in results)
                {
                    log.Info(
                        $@"{result.SchemaItemType.SchemaItemDescription()?.Name} {
                            result.ItemName} {result.Remark}");
                }
            }
        }

        private static int TestDatabase(DBTestArguments arguments)
        {
            OrigamSettingsCollection configurations;
            try
            {
                configurations =
                    ConfigurationManager.GetAllConfigurations();
                if (configurations.Count != 1)
                {
                    return SetTestDatabaseReturn(false);
                }
            } catch
            {
                return SetTestDatabaseReturn(false);
            }
            OrigamSettings settings = configurations[0];
            string connString = settings.DataConnectionString;
            bool result = false;
            for (int i = 0; i < arguments.tries; i++)
            {
                try
                {
                    using (var connection = new SqlConnection(connString))
                    {
                        var query = arguments.sqlCommand;
                        var command = new SqlCommand(query, connection);
                        connection.Open();
                        var info = command.ExecuteScalar().ToString();
                        if (info != null)
                        {
                            result = true;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Info("Failure:", ex);
                }
                Thread.Sleep(arguments.delay);
            }
            return SetTestDatabaseReturn(result);
        }

        private static int SetTestDatabaseReturn(bool bol)
        {
            Console.Write(bol);
            return Convert.ToInt32(bol);
        }
    }
}