﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using System;
using System.Threading.Tasks;

namespace PartsUnlimited.WebJobs.ProcessOrder
{
    public class Program
    {
        public int Main()
        {
            var builder = new ConfigurationBuilder();
            builder.Add(new JsonConfigurationSource { Path = "config.json" });
            var config = builder.Build();
            var webjobsConnectionString = config["Data:AzureWebJobsStorage:ConnectionString"];
            var dbConnectionString = config["Data:DefaultConnection:ConnectionString"];
            if (string.IsNullOrWhiteSpace(webjobsConnectionString))
            {
                Console.WriteLine("The configuration value for Azure Web Jobs Connection String is missing.");
                return 10;
            }

            if (string.IsNullOrWhiteSpace(dbConnectionString))
            {
                Console.WriteLine("The configuration value for Database Connection String is missing.");
                return 10;
            }

            //var jobHostConfig = new JobHostConfiguration(config["Data:AzureWebJobsStorage:ConnectionString"]);
            //var host = new JobHost(null);// todo
            //var methodInfo = typeof(Functions).GetMethods().First();

            //host.Call(methodInfo);
            return 0;
        }
    }
}
