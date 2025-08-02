using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PartyFinderReborn.Services
{
    public class DebouncedException : Exception
    {
        public DebouncedException(string message) : base(message) { }
    }

    public class ApiDebounceService
    {
        private readonly ConcurrentDictionary<ApiOperationType, DateTime> _lastExecution;
        private readonly IReadOnlyDictionary<ApiOperationType, TimeSpan> _cooldowns;
        private readonly DateTime _initializationTime;
        private readonly TimeSpan _initializationGracePeriod;

        public ApiDebounceService(IReadOnlyDictionary<ApiOperationType, TimeSpan>? cooldownOverrides = null, TimeSpan? initializationGracePeriod = null)
        {
            _lastExecution = new ConcurrentDictionary<ApiOperationType, DateTime>();
            _cooldowns = cooldownOverrides ?? new Dictionary<ApiOperationType, TimeSpan>
            {
                { ApiOperationType.Read, ApiOperationTypeDefaults.Read },
                { ApiOperationType.Write, ApiOperationTypeDefaults.Write },
                { ApiOperationType.QuickAction, ApiOperationTypeDefaults.QuickAction }
            };
            _initializationTime = DateTime.UtcNow;
            _initializationGracePeriod = initializationGracePeriod ?? TimeSpan.FromSeconds(10); // 10 second grace period by default
        }

        public bool CanExecute(ApiOperationType op, out double secondsLeft)
        {
            secondsLeft = 0;
            
            // Allow all operations during initialization grace period
            if (IsInInitializationGracePeriod())
            {
                return true;
            }
            
            if (_lastExecution.TryGetValue(op, out var lastExecTime))
            {
                var cooldown = _cooldowns[op];
                var timeSinceLastExec = DateTime.UtcNow - lastExecTime;
                if (timeSinceLastExec < cooldown)
                {
                    secondsLeft = (cooldown - timeSinceLastExec).TotalSeconds;
                    return false;
                }
            }
            return true;
        }

        public void MarkExecuted(ApiOperationType op)
        {
            _lastExecution[op] = DateTime.UtcNow;
        }

        public double SecondsRemaining(ApiOperationType op)
        {
            // No cooldown during initialization grace period
            if (IsInInitializationGracePeriod())
            {
                return 0;
            }
            
            if (_lastExecution.TryGetValue(op, out var lastExecTime))
            {
                var cooldown = _cooldowns[op];
                var timeSinceLastExec = DateTime.UtcNow - lastExecTime;
                if (timeSinceLastExec < cooldown)
                {
                    return (cooldown - timeSinceLastExec).TotalSeconds;
                }
            }
            return 0;
        }
        
        /// <summary>
        /// Check if we're still within the initialization grace period where debouncing is disabled
        /// </summary>
        private bool IsInInitializationGracePeriod()
        {
            return DateTime.UtcNow - _initializationTime < _initializationGracePeriod;
        }

        public async Task<T?> RunIfAllowedAsync<T>(ApiOperationType op, Func<Task<T?>> action)
        {
            if (!CanExecute(op, out var secondsLeft))
            {
                throw new DebouncedException($"Operation is currently cooling down. Try again in {secondsLeft} seconds.");
            }

            MarkExecuted(op);
            return await action();
        }
    }
}
