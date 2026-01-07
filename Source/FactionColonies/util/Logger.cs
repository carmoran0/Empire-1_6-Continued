using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace FactionColonies
{
    public static class Logger
    {
        public static void DebugLog(string msg, LogMessageType messageType = LogMessageType.Message)
        {
            string output = "[Empire] " + msg;
            if (messageType == LogMessageType.Message)
            {
                //if (RFPSettings.printDebug)
                {
                    Log.Message(output);
                }
            }
            else if (messageType == LogMessageType.Warning)
            {
                Log.Warning(output);
            }
            else if (messageType == LogMessageType.Error)
            {
                Log.Error(output);
            }
        }
    }
}
