﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.VisualStudio.Web.CodeGeneration.Contracts.FileSystemChange;
using Microsoft.VisualStudio.Web.CodeGeneration.Contracts.Messaging;
using Microsoft.VisualStudio.Web.CodeGeneration.Contracts.ProjectModel;
using Microsoft.VisualStudio.Web.CodeGeneration.Utils.Messaging;
using Newtonsoft.Json.Linq;

namespace Microsoft.VisualStudio.Web.CodeGeneration.Design
{
    public class Program
    {
        public const string TOOL_NAME = "dotnet-aspnet-codegenerator-design";
        private const string APPNAME = "Code Generation";

        private static ConsoleLogger _logger;

        public static void Main(string[] args)
        {
            _logger = new ConsoleLogger();
            _logger.LogMessage($"Command Line: {string.Join(" ", args)}", LogMessageLevel.Trace);

            //DotnetToolDispatcher.EnsureValidDispatchRecipient(ref args);
            Execute(args, _logger);
        }

        private static void Execute(string[] args, ConsoleLogger logger)
        {
            var app = new CommandLineApplication(false)
            {
                Name = APPNAME,
                Description = Resources.AppDesc
            };

            // Define app Options;
            app.HelpOption("-h|--help");
            var projectPath = app.Option("-p|--project", Resources.ProjectPathOptionDesc, CommandOptionType.SingleValue);
            var appConfiguration = app.Option("-c|--configuration", Resources.ConfigurationOptionDesc, CommandOptionType.SingleValue);
            var framework = app.Option("-tfm|--target-framework", Resources.TargetFrameworkOptionDesc, CommandOptionType.SingleValue);
            var buildBasePath = app.Option("-b|--build-base-path", "", CommandOptionType.SingleValue);
            var dependencyCommand = app.Option("--no-dispatch", "", CommandOptionType.NoValue);
            var port = app.Option("--port-number", "", CommandOptionType.SingleValue);
            var noBuild = app.Option("--no-build", "", CommandOptionType.NoValue);
            var simMode = app.Option("--simulation-mode", Resources.SimulationModeOptionDesc, CommandOptionType.NoValue);

            app.OnExecute(async () =>
            {

                string project = projectPath.Value();
                if (string.IsNullOrEmpty(project))
                {
                    project = Directory.GetCurrentDirectory();
                }
                project = Path.GetFullPath(project);
                var configuration = appConfiguration.Value();

                var portNumber = int.Parse(port.Value());
                using (var client = await ScaffoldingClient.Connect(portNumber, logger))
                {
                    var projectInformation = GetProjectInformationFromServer(logger, portNumber, client);

                    var codeGenArgs = ToolCommandLineHelper.FilterExecutorArguments(args);
                    var isSimulationMode = ToolCommandLineHelper.IsSimulationMode(args);
                    CodeGenCommandExecutor executor = new CodeGenCommandExecutor(projectInformation,
                        codeGenArgs,
                        configuration,
                        logger,
                        isSimulationMode);

                    var exitCode = executor.Execute((changes) => SendFileSystemChanges(changes, client));

                    client.Send(new Message() { MessageType = MessageTypes.Scaffolding_Completed });

                    return exitCode;
                }
            });

            app.Execute(args);
        }

        private static void SendFileSystemChanges(IEnumerable<FileSystemChangeInformation> changes, ScaffoldingClient client)
        {
            if (changes == null)
            {
                return;
            }

            foreach (var change in changes)
            {
                var message = new Message()
                {
                    MessageType = MessageTypes.FileSystemChangeInformation
                };
                message.Payload = JToken.FromObject(change);

                client.Send(message);
            }
        }

        private static IProjectContext GetProjectInformationFromServer(ILogger logger, int portNumber, ScaffoldingClient client)
        {

            var messageHandler = new ScaffoldingMessageHandler(logger, "ScaffoldingClient");
            client.MessageReceived += messageHandler.HandleMessages;

            var message = new Message()
            {
                MessageType = MessageTypes.ProjectInfoRequest
            };

            client.Send(message);
            // Read the project Information
            client.ReadMessage();

            var projectInfo = messageHandler.ProjectInfo;

            if (projectInfo == null)
            {
                throw new InvalidOperationException(Resources.ProjectInformationError);
            }

            return projectInfo;
        }
    }
}
