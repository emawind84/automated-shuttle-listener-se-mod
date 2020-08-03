using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        const string ScriptPrefixTag = "SHUTTLE_LISTENER";

        const string DisplayTerminalTag = ScriptPrefixTag + ":DisplayTerminal";

        /// <summary>
        /// whether to use real time (second between calls) or pure UpdateFrequency
        /// for update frequency
        /// </summary>
        readonly bool USE_REAL_TIME = false;
        /// <summary>
        /// Defines the FREQUENCY.
        /// </summary>
        const UpdateFrequency FREQUENCY = UpdateFrequency.Update100;
        /// <summary>
        /// How often the script should update in milliseconds
        /// </summary>
        const int UPDATE_REAL_TIME = 1000;
        /// <summary>
        /// The maximum run time of the script per call.
        /// Measured in milliseconds.
        /// </summary>
        const double MAX_RUN_TIME = 35;
        /// <summary>
        /// The maximum percent load that this script will allow
        /// regardless of how long it has been executing.
        /// </summary> 
        const double MAX_LOAD = 0.8;
        /// <summary>
        /// A wrapper for the <see cref="Echo"/> function that adds the log to the stored log.
        /// This allows the log to be remembered and re-outputted without extra work.
        /// </summary>
        public Action<string> EchoR;

        #region Script state & storage

        /// <summary>
        /// Handle Custom Data settings
        /// </summary>
        public MyIni _ini = new MyIni();
        /// <summary>
        /// Handle script arguments
        /// </summary>
        MyCommandLine _commandLine = new MyCommandLine();
        /// <summary>
        /// A list of commands available to execute using script argument
        /// </summary>
        Dictionary<string, Action> _commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// The time we started the last cycle at.
        /// If <see cref="USE_REAL_TIME"/> is <c>true</c>, then it is also used to track
        /// when the script should next update
        /// </summary>
        DateTime currentCycleStartTime;
        /// <summary>
        /// The time the previous step ended
        /// </summary>
        DateTime previousStepEndTime;
        /// <summary>
        /// The time to wait before starting the next cycle.
        /// Only used if <see cref="USE_REAL_TIME"/> is <c>true</c>.
        /// </summary>
        TimeSpan cycleUpdateWaitTime = new TimeSpan(0, 0, 0, 0, UPDATE_REAL_TIME);
        /// <summary>
        /// The total number of calls this script has had since compilation.
        /// </summary>
        long totalCallCount;
        /// <summary>
        /// The text to echo at the start of each call.
        /// </summary>
        string scriptUpdateText;
        /// <summary>
        /// The current step in the TIM process cycle.
        /// </summary>
        int processStep;
        /// <summary>
        /// All of the process steps that TIM will need to take,
        /// </summary>
        readonly Action[] processSteps;
        /// <summary>
        /// Stores the output of Echo so we can effectively ignore some calls
        /// without overwriting it.
        /// </summary>
        public StringBuilder echoOutput = new StringBuilder();
        
        /// <summary>
        /// Defines the terminalCycle.
        /// </summary>
        IEnumerator<bool> terminalCycle;

        DebugTerminal debugTerminals;

        DisplayTerminal informationTerminals;

        const string STATE_BROADCAST_TAG = "SHUTTLE_STATE";

        IMyBroadcastListener BroadcastListener { get; }

        public Dictionary<long, ShuttleInfo> Shuttles { get; } = new Dictionary<long, ShuttleInfo>();

        #endregion

        #region Properties

        /// <summary>
        /// The length of time we have been executing for.
        /// Measured in milliseconds.
        /// </summary>
        int ExecutionTime
        {
            get { return (int)((DateTime.Now - currentCycleStartTime).TotalMilliseconds + 0.5); }
        }

        /// <summary>
        /// The current percent load of the call.
        /// </summary>
        double ExecutionLoad
        {
            get { return Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount; }
        }

        #endregion

        #region Version

        const string SCRIPT_NAME = "ED's Automated Shuttle";
        // current script version
        const int VERSION_MAJOR = 1, VERSION_MINOR = 0, VERSION_REVISION = 1;
        /// <summary>
        /// Current script update time.
        /// </summary>
        const string VERSION_UPDATE = "2020-07-30";
        /// <summary>
        /// A formatted string of the script version.
        /// </summary>
        readonly string VERSION_NICE_TEXT = string.Format("v{0}.{1}.{2} ({3})", VERSION_MAJOR, VERSION_MINOR, VERSION_REVISION, VERSION_UPDATE);

        #endregion

        #region Format Strings

        /// <summary>
        /// The format for the text to echo at the start of each call.
        /// </summary>
        const string FORMAT_UPDATE_TEXT = "{0}\n{1}\nLast run: #{{0}} at {{1}}";

        #endregion

        public Program()
        {
            // init echo wrapper
            EchoR = log =>
            {
                echoOutput.AppendLine(log);
                Echo(log);
            };

            debugTerminals = new DebugTerminal(this);
            informationTerminals = new DisplayTerminal(this, blk => MyIni.HasSection(blk.CustomData, DisplayTerminalTag) && CollectSameConstruct(blk));
            terminalCycle = SetTerminalCycle();

            this.BroadcastListener = this.IGC.RegisterBroadcastListener(STATE_BROADCAST_TAG);
            this.BroadcastListener.SetMessageCallback();

            // initialise the process steps we will need to do
            processSteps = new Action[]
            {
                ListenBroadcastMessages
            };

            Runtime.UpdateFrequency = FREQUENCY;

            EchoR(string.Format("Compiled {0} {1}", SCRIPT_NAME, VERSION_NICE_TEXT));

            // format terminal info text
            scriptUpdateText = string.Format(FORMAT_UPDATE_TEXT, SCRIPT_NAME, VERSION_NICE_TEXT);
        }

        public void Save()
        {
            
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (USE_REAL_TIME)
            {
                DateTime n = DateTime.Now;
                if (n - currentCycleStartTime >= cycleUpdateWaitTime)
                    currentCycleStartTime = n;
                else
                {
                    Echo(echoOutput.ToString()); // ensure that output is not lost
                    return;
                }
            }
            else
            {
                currentCycleStartTime = DateTime.Now;
            }

            echoOutput.Clear();

            // output terminal info
            EchoR(string.Format(scriptUpdateText, ++totalCallCount, currentCycleStartTime.ToString("h:mm:ss tt")));

            bool commandInvoked = false;
            if (_commandLine.TryParse(argument))
            {
                Action commandAction;

                // Retrieve the first argument. Switches are ignored.
                string command = _commandLine.Argument(0);

                // Now we must validate that the first argument is actually specified, 
                // then attempt to find the matching command delegate.
                if (command == null)
                {
                    Echo("No command specified");
                }
                else if (_commands.TryGetValue(command, out commandAction))
                {
                    // We have found a command. Invoke it.
                    commandAction();
                    commandInvoked = true;
                }
                else
                {
                    Echo($"Unknown command {command}");
                }
            }

            if (processStep == processSteps.Length)
            {
                processStep = 0;
            }
            int processStepTmp = processStep;
            bool didAtLeastOneProcess = false;

            try
            {
                if (!commandInvoked) processSteps[processStep]();
                didAtLeastOneProcess = true;
            }
            catch (PutOffExecutionException) { }
            catch (Exception ex)
            {
                // if the process step threw an exception, make sure we print the info
                // we need to debug it
                string err = "An error occured,\n" +
                    "please give the following information to the developer:\n" +
                    string.Format("Current step on error: {0}\n{1}", processStep, ex.ToString().Replace("\r", ""));
                EchoR(err);
                throw ex;
            }

            // we save last ship position and previous step completed time after every step
            if (processStep != processStepTmp)
            {
                previousStepEndTime = DateTime.Now;
            }

            string stepText;
            int theoryProcessStep = processStep == 0 ? processSteps.Count() : processStep;
            int exTime = ExecutionTime;
            double exLoad = Math.Round(100.0f * ExecutionLoad, 1);
            if (processStep == 0 && processStepTmp == 0 && didAtLeastOneProcess)
                stepText = "all steps";
            else if (processStep == processStepTmp)
                stepText = string.Format("step {0} partially", processStep);
            else if (theoryProcessStep - processStepTmp == 1)
                stepText = string.Format("step {0}", processStepTmp);
            else
                stepText = string.Format("steps {0} to {1}", processStepTmp, theoryProcessStep - 1);
            EchoR(string.Format("Completed {0} in {1}ms\n{2}% load ({3} instructions)",
                stepText, exTime, exLoad, Runtime.CurrentInstructionCount));

            if (!terminalCycle.MoveNext())
            {
                terminalCycle.Dispose();
            }
        }

        /// <summary>
        /// The SetTerminalCycle.
        /// </summary>
        /// <returns>The <see cref="IEnumerator{bool}"/>.</returns>
        IEnumerator<bool> SetTerminalCycle()
        {
            while (true)
            {
                yield return debugTerminals.Run();
                yield return informationTerminals.Run();
            }
        }

        void ListenBroadcastMessages()
        {
            if (this.BroadcastListener.HasPendingMessage) {
                EchoR("Pending messages");
            }
            while (this.BroadcastListener.HasPendingMessage)
            {
                var message = this.BroadcastListener.AcceptMessage();
                var data = message.As<MyTuple<long, string, Vector3D, string>>();
                var shuttleInfo = new ShuttleInfo
                {
                    ID = data.Item1,
                    Name = data.Item2,
                    Position = data.Item3,
                    Message = data.Item4
                };

                Shuttles[shuttleInfo.ID] = shuttleInfo;

                EchoR(shuttleInfo.ToString());
                throw new PutOffExecutionException();
            }
            processStep++;
        }

        public struct ShuttleInfo
        {
            public long ID { get; set; }
            public string Name { get; set; }
            public Vector3D Position { get; set; }
            public string Message { get; set; }

            public override string ToString()
            {
                return $"{this.Name}" + Environment.NewLine +
                    $"Position: {this.Position}" + Environment.NewLine +
                    $"Msg: {this.Message}";
            }
        }
    }
}
