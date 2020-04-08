﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Classifiers;
using SnaffCore.Concurrency;

namespace SnaffCore.ShareScan
{
    public class TreeWalker
    {
        private FileScanner FileScanner { get; set; }

        public TreeWalker(string shareRoot)
        {
            BlockingMq Mq = BlockingMq.GetMq();
            Config.Config myConfig = Config.Config.GetConfig();
            if (shareRoot == null)
            {
                Mq.Trace("A null made it into TreeWalker. Wtf.");
                return;
            }

            Mq.Trace("About to start a TreeWalker on share " + shareRoot);
            FileScanner = new FileScanner();
            WalkTree(shareRoot);
            Mq.Trace("Finished TreeWalking share " + shareRoot);
        }

        public void WalkTree(string shareRoot)
        {
            BlockingMq Mq = BlockingMq.GetMq();
            Config.Config myConfig = Config.Config.GetConfig();
            TaskFactory taskFactory = LimitedConcurrencyLevelTaskScheduler.GetShareScannerTaskFactory();
            CancellationTokenSource cts = LimitedConcurrencyLevelTaskScheduler.GetShareScannerCts();
            try
            {
                // Walks a tree checking files and generating results as it goes.
                var dirs = new Stack<string>(20);

                if (!Directory.Exists(shareRoot))
                {
                    return;
                }

                dirs.Push(shareRoot);

                while (dirs.Count > 0)
                {
                    var currentDir = dirs.Pop();
                    string[] subDirs;
                    try
                    {
                        subDirs = Directory.GetDirectories(currentDir);
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        Mq.Trace(e.ToString());
                        continue;
                    }
                    catch (DirectoryNotFoundException e)
                    {
                        Mq.Trace(e.Message);
                        continue;
                    }
                    catch (Exception e)
                    {
                        Mq.Trace(e.Message);
                        continue;
                    }

                    string[] files = null;
                    try
                    {
                        files = Directory.GetFiles(currentDir);
                    }
                    catch (UnauthorizedAccessException e)
                    {
                        Mq.Trace(e.Message);
                        continue;
                    }
                    catch (DirectoryNotFoundException e)
                    { 
                        Mq.Trace(e.Message);
                        continue;
                    }
                    catch (Exception e)
                    {
                        Mq.Trace(e.Message);
                        continue;
                    }

                    // check if we actually like the files
                    foreach (string file in files)
                    {
                        var t = taskFactory.StartNew(() =>
                        {
                            try
                            {
                                FileScanner.ScanFile(file);
                            }
                            catch (Exception e)
                            {
                                Mq.Trace(e.ToString());
                            }
                        }, cts.Token);
                    }

                    // Push the subdirectories onto the stack for traversal if they aren't on any discard-lists etc.
                    foreach (var dirStr in subDirs)
                    {
                        foreach (Classifier dirClassifier in myConfig.Options.DirClassifiers)
                        {
                            DirResult dirResult = dirClassifier.ClassifyDir(dirStr);
                            // TODO: concurrency uplift: when there is a pooled concurrency queue, just add the dir as a job to the queue
                            if (dirResult.ScanDir) { dirs.Push(dirStr);}
                        }

                    }
                }
            }
            catch (Exception e)
            {
                Mq.Error(e.ToString());
            }
        }
    }
}