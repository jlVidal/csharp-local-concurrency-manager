using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JLVidal.Concurrent
{
    public abstract class TimeoutStrategyBase<T>
    {
        public abstract void TimeoutReached(T key);
    }

    public class TimeoutStrategy
    {
        private class QuietStrategy<T> : TimeoutStrategyBase<T>
        {
            public override void TimeoutReached(T key)
            {
            }

            public static readonly TimeoutStrategyBase<T> Instance = new QuietStrategy<T>();
        }

        private class SimpleTimeoutExceptionStrategy<T> : TimeoutStrategyBase<T>
        {
            public override void TimeoutReached(T key)
            {
                throw new TimeoutException(string.Format("Error='Semaphore timeout (ConcurrencyManager class, On GetToken method)' Key='{0}'", key));
            }

            public static readonly TimeoutStrategyBase<T> Instance = new SimpleTimeoutExceptionStrategy<T>();
        }

        private class CustomTimeoutStrategy<T> : TimeoutStrategyBase<T>
        {
            private Action<T> _callBack;

            public CustomTimeoutStrategy(Action<T> callBack)
            {
                if (callBack == null)
                    throw new ArgumentNullException("callBack");

                this._callBack = callBack;
            }

            public override void TimeoutReached(T key)
            {
                _callBack(key);
            }
        }

        private class CustomTimeoutExceptionStrategy<T> : TimeoutStrategyBase<T>
        {
            private Func<T, string> exceptionMessageFunc;

            public CustomTimeoutExceptionStrategy(Func<T, string> exceptionMessageFunc)
            {
                if (exceptionMessageFunc == null)
                    throw new ArgumentNullException("exceptionMessageFunc");

                this.exceptionMessageFunc = exceptionMessageFunc;
            }

            public override void TimeoutReached(T key)
            {
                throw new TimeoutException(exceptionMessageFunc(key));
            }
        }

        public static TimeoutStrategyBase<T> GetQuietMode<T>()
        {
            return QuietStrategy<T>.Instance;
        }

        public static TimeoutStrategyBase<T> GetExceptionMode<T>()
        {
            return SimpleTimeoutExceptionStrategy<T>.Instance;
        }

        public static TimeoutStrategyBase<T> GetExceptionMode<T>(Func<T, string> exceptionMessageFunc)
        {
            return new CustomTimeoutExceptionStrategy<T>(exceptionMessageFunc);
        }
        public static TimeoutStrategyBase<T> GetCustomMode<T>(Action<T> callback)
        {
            return new CustomTimeoutStrategy<T>(callback);
        }
    }

    public class LocalConcurrencyManager<T>
    {
        private int _timeout;

        public int Timeout
        {
            get { return _timeout; }
            set { _timeout = Math.Max(-1, value); }
        }

        private readonly ConcurrentDictionary<T, SemaphoreSlim> _dict;
        private TimeoutStrategyBase<T> _timeoutStrategy;

        public LocalConcurrencyManager() : this(0)
        {
        }

        public LocalConcurrencyManager(int timeoutMs) : this(timeoutMs, EqualityComparer<T>.Default)
        {
        }


        public LocalConcurrencyManager(int timeoutMs, IEqualityComparer<T> comparer) : this(timeoutMs, null, TimeoutStrategy.GetExceptionMode<T>())
        {

        }

        public LocalConcurrencyManager(int timeoutMs, IEqualityComparer<T> comparer, TimeoutStrategyBase<T> strategy)
        {
            this.Timeout = timeoutMs;
            this._dict = new ConcurrentDictionary<T, SemaphoreSlim>(comparer ?? EqualityComparer<T>.Default);
            this._timeoutStrategy = strategy ?? TimeoutStrategy.GetExceptionMode<T>();
        }

        public IDisposable GetToken(T key)
        {
            object obj = key;
            if (obj == null)
                throw new NullReferenceException("key");

            SemaphoreSlim sem = null;
            var syncronization = _dict.GetOrAdd(key, str =>
                {
                    sem = new SemaphoreSlim(1, 1);
                    return sem;
                });

            if (sem != null && sem != syncronization)
                sem.Dispose();

            var enterSyncronization = syncronization.Wait(Timeout);
            if (!enterSyncronization)
            {
                _timeoutStrategy.TimeoutReached(key);
                return FakeToken.Empty;
            }

            var wrapper = new InlineIntWrapperOptimizer(syncronization.Release);
            return new InlineToken(key, wrapper.CallWrappedMethod);
        }

        private class InlineIntWrapperOptimizer
        {
            private Func<int> innerOnDispose;

            public InlineIntWrapperOptimizer(Func<int> onDispose)
            {
                this.innerOnDispose = onDispose;
            }

            public void CallWrappedMethod(InlineToken token)
            {
                innerOnDispose();
            }
        }

        private class InlineVoidWrapperOptimizer
        {
            private Action innerOnDispose;

            public InlineVoidWrapperOptimizer(Action onDispose)
            {
                this.innerOnDispose = onDispose;
            }

            public void Call(InlineToken token)
            {
                innerOnDispose();
            }
        }

        internal sealed class FakeToken : IConcurrencyManagerToken
        {
            private FakeToken()
            {

            }

            public void Dispose()
            {

            }

            private static readonly IDisposable empty = new FakeToken();

            public static IDisposable Empty
            {
                get { return empty; }
            }

            public bool TimeoutReached
            {
                get
                {
                    return true;
                }
            }
        }

        internal sealed class InlineToken : IConcurrencyManagerToken
        {
            private readonly T key;
            private Action<InlineToken> onDispose;

            public InlineToken(T key, Action<InlineToken> onDispose)
            {
                this.key = key;
                this.onDispose = onDispose;
            }

            public T Key
            {
                get { return key; }
            }

            public bool TimeoutReached
            {
                get
                {
                    return false;
                }
            }

            public void Dispose()
            {
                try
                {
                    onDispose(this);
                }
                finally
                {
                    onDispose = null;
                }
            }

        }
    }

    public interface IConcurrencyManagerToken : IDisposable
    {
        bool TimeoutReached { get; }
    }
}
