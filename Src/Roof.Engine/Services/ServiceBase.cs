using System;
using System.Threading.Tasks;

namespace Roof.Engine.Services
{
    internal abstract class ServiceBase
    {
        internal abstract Task Initialize();

        internal abstract void Start();

        internal virtual Task<bool> Terminate()
        {
            return Task.FromResult(true);
        }
    }
}