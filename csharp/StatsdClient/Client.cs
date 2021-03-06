
using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.IO;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Timers;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;


namespace Statsd
{
    public class StatsdPipe : IStatsdClient
    {
        private static readonly Random RAND = new Random();

        private SendDelegate doSend = new SendDelegate(new AFTransport().Send);

        public SendDelegate Send
        {
            get { return this.doSend; }
            set { this.doSend = value; }
        }

        private IStrategy strategy = new InstantStrategy();

        public IStrategy Strategy
        {
            set
            {
                if (value != null)
                {
                    this.strategy = value;
                }
            }
        }
        
        #region Gauge Functions

        //Gauge Functions
        public bool Gauge(string key, int value)
        {
            return Gauge(null, key, value);
        }

        public bool Gauge(string message, string key, int value)
        {
            return strategy.Emit<GaugeBucket>(this.doSend, key, value, message);
        }

        #endregion

        #region Timing Functions

        //Timing Functions
        public bool Timing(string key, int value)
        {
            return Timing(null, key, value);
        }

        public bool Timing(string message, string key, int value)
        {
            return strategy.Emit<TimerBucket>(this.doSend, key, value, message);
        }

        #endregion

        #region Counter Functions

        public bool Decrement(params string[] keys)
        {
            return UpdateCount(-1, keys);
        }

        public bool Increment(params string[] keys)
        {
            return UpdateCount(1, keys);
        }

        public bool UpdateCount(int magnitude, params string[] keys)
        {
            return UpdateCount(null, magnitude, 1.0, keys);
        }

        public bool UpdateCount(int magnitude, double sampleRate, params string[] keys)
        {
            return UpdateCount(null, magnitude, sampleRate, keys);
        }

        public bool UpdateCount(string message, int magnitude, params string[] keys)
        {
            return UpdateCount(message, magnitude, 1.0, keys);
        }

        public bool UpdateCount(string message, int magnitude, double sampleRate, params string[] keys)
        {
            if (sampleRate < 1)
            {
                if (RAND.NextDouble() > sampleRate)
                {
                    return true;
                }
                else
                {
                    magnitude = Convert.ToInt32(magnitude/sampleRate);
                }
            }
            bool retvalue = true;
            foreach (string key in keys)
            {
                bool success = this.strategy.Emit<CounterBucket>(this.doSend, key, magnitude, message);
                retvalue &= success;
            }
            return retvalue;
        }
        
        #endregion
    }


    public class GeyserStrategy : IStrategy
    {
        private static BucketBuffer buffer = new BucketBuffer();
        private static Timer schedule = new Timer();
        public double Interval = 20000;
        private static SendDelegate doSend;

        static GeyserStrategy()
        {
            Process thisProcess = Process.GetCurrentProcess();
            schedule.AutoReset = false;
            thisProcess.Exited += new EventHandler(Flush);
            schedule.Elapsed += new ElapsedEventHandler(Flush);
        }

        private static void Flush(object sender, System.EventArgs e)
        {
            if (!buffer.IsEmpty())
            {
                Dictionary<String, IBucket> dumpcellar = buffer.Dump();
                foreach (IBucket bucket in dumpcellar.Values)
                {
                    doSend(bucket.ToString());
                }
            }
        }
        [MethodImpl(MethodImplOptions.Synchronized)]
        public bool Emit<T>(SendDelegate doSend, string bucketname, int value, string message) 
            where T : IBucket
        {
            GeyserStrategy.doSend = doSend;
            buffer.Accumulate<T>(bucketname, value, message);
            
            if (!schedule.Enabled)
            {
                schedule.Interval = this.Interval;
                schedule.Start();
            }

            return true;
        }
    }

    public class InstantStrategy : IStrategy
    {
        public bool Emit<T>(SendDelegate doSend, string bucketname, int value, string message) 
            where T : IBucket
        {
            T bucket = (T) Activator.CreateInstance(typeof(T));
            bucket.Name = bucketname;
            bucket.Infuse(value, message);            
            return doSend(bucket.ToString());
        }
    }
}
