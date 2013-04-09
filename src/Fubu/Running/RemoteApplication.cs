﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Bottles.Services.Messaging;
using FubuCore;
using FubuCore.CommandLine;
using OpenQA.Selenium;

namespace Fubu.Running
{
    public class RemoteApplication : IListener<ApplicationStarted>, IListener<InvalidApplication>, IApplicationObserver
    {
        public static readonly FileMatcher FileMatcher;

        private readonly ManualResetEvent _reset = new ManualResetEvent(false);
        private IWebDriver _driver;
        private ApplicationRequest _input;
        private bool _opened;
        private RemoteFubuMvcProxy _proxy;
        private FubuMvcApplicationFileWatcher _watcher;


        static RemoteApplication()
        {
            string location = Assembly.GetExecutingAssembly().Location;
            string directory = Path.GetDirectoryName(location);
            if (Directory.Exists(directory))
            {
                FileMatcher = FileMatcher.ReadFromFile(directory.AppendPath(FileMatcher.File));
            }
            else
            {
                FileMatcher = FileMatcher.ReadFromFile(FileMatcher.File);
            }
        }

        public void RefreshContent()
        {
            if (_driver != null) _driver.Navigate().Refresh();
        }

        public void RecycleAppDomain()
        {
            _watcher.StopWatching();
            if (_proxy != null)
            {
                _proxy.SafeDispose();
            }

            start();
        }

        public void RecycleApplication()
        {
            _watcher.StopWatching();
            _proxy.Recycle();
        }

        public void Receive(ApplicationStarted message)
        {
            Console.WriteLine("Started application {0} at url {1} at {2}", message.ApplicationName, message.HomeAddress,
                              message.Timestamp);

            if (_input.OpenFlag && !_opened)
            {
                _opened = true;
                Process.Start(message.HomeAddress);
            }

            if (_input.WatchedFlag)
            {
                if (_driver == null)
                {
                    _driver = _input.BuildBrowser();
                    _driver.Navigate().GoToUrl(message.HomeAddress);
                }
                else
                {
                    _driver.Navigate().Refresh();
                }
            }

            _watcher.StartWatching(_input.DirectoryFlag, message.BottleContentFolders);

            _reset.Set();
        }

        public void Receive(InvalidApplication message)
        {
            ConsoleWriter.Write(ConsoleColor.Red, message.Message);

            if (message.Applications != null && message.Applications.Any())
            {
                Console.WriteLine("Found applications:  " + message.Applications.Join(", "));
            }

            if (message.ExceptionText.IsNotEmpty())
            {
                ConsoleWriter.Write(ConsoleColor.Yellow, message.ExceptionText);
            }

            throw new Exception("Application Failed!");
        }

        public void Start(ApplicationRequest input)
        {
            _input = input;

            _watcher = new FubuMvcApplicationFileWatcher(this, FileMatcher);

            start();
        }

        private void start()
        {
            _reset.Reset();
            _proxy = new RemoteFubuMvcProxy(_input);
            _proxy.Start(this);

            _reset.WaitOne();
        }
    }
}