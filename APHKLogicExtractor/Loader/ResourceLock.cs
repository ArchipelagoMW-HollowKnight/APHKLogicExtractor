namespace APHKLogicExtractor.Loader;

public class ResourceLock<T>(T resource)
{
    private readonly SemaphoreSlim lockable = new(1);
    private T resource = resource;

    public async Task<LockGuard> Enter(TimeSpan timeout)
    {
        if (await this.lockable.WaitAsync(timeout))
        {
            return new LockGuard(this);
        }

        throw new InvalidOperationException("Unable to get lock to requested resource");
    }

    public class LockGuard(ResourceLock<T> resourceLock) : IDisposable
    {
        private ResourceLock<T>? resourceLock = resourceLock;

        public T Value
        {
            get
            {
                if (this.resourceLock == null)
                    throw new InvalidOperationException("Read from released lock");
                return this.resourceLock.resource;
            }
            set
            {
                if (this.resourceLock == null)
                    throw new InvalidOperationException("Write into released lock");
                this.resourceLock.resource = value;
            }
        }

        public void Dispose()
        {
            this.resourceLock?.lockable.Release();
            this.resourceLock = null;
        }
    }
}
