using SimpleLogger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LoggerTest
{
    class Program
    {
        static Logger logger = Logger.Instance;

        static void Main(string[] args)
        {
            logger.Start();
            logger.Info("TEST");
            logger.Dispose();
        }
    }
}
