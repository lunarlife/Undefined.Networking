namespace Networking.Loggers
{

    public abstract class Logger
    {
        public abstract void Info(string info);
        public abstract void Warning(string warning);
        public abstract void Error(string error);
    }
}