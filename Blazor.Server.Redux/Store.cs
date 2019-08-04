using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Blazor.Server.Redux
{
    public class Store<TState, TAction> : IDisposable
    {
        #region Fields
        private readonly Reducer<TState, TAction> _rootReducer;
        private readonly ReduxOptions<TState, TAction> _options;
        private IList<HistoricStateEntry<TState, object>> _past;
        private IList<HistoricStateEntry<TState, object>> _future;
        private IUriHelper _uriHelper;
        private Func<string, TAction> _locationActionCreator;
        private string _currentLocation;
        private readonly object _syncRoot = new object();
        private SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);
        #endregion

        #region Properties
        /// <summary>
        /// The current state of the application.
        /// </summary>
        public TState State { get; private set; }
        /// <summary>
        /// Handler invoked when state changes.
        /// </summary>
        public event EventHandler OnStateChanged;
        #endregion

        public Store(TState initialState, Reducer<TState, TAction> rootReducer, ReduxOptions<TState, TAction> options)
        {
            _rootReducer = rootReducer;
            _options = options;

            State = initialState;

            _past = new List<HistoricStateEntry<TState, object>>();
            AddHistoricStateEntry(new HistoricStateEntry<TState, object>(initialState));
            _future = new List<HistoricStateEntry<TState, object>>();
        }

        /// <summary>
        /// Dispatches an action to state.
        /// </summary>
        /// <param name="action">The action.</param>
        public async Task Dispatch(TAction action)
        {
            await semaphoreSlim.WaitAsync();

            try
            {
                State = await _rootReducer(State, action);
                AddHistoricStateEntry(new HistoricStateEntry<TState, object>(State, action));
                _future.Clear();
            }
            finally
            {
                semaphoreSlim.Release();
            }

            InvokeOnStateChanged(null);
        }

        private void InvokeOnStateChanged(EventArgs e)
        {
            EventHandler handler = OnStateChanged;
            handler?.Invoke(this, e);
        }

        #region State History Management
        /// <summary>
        /// Safely add a historic entry to the history stack while adhering to the limitations of the ReduxOptions.
        /// </summary>
        /// <param name="HistoricStateEntry">The entry to add to the history stack.</param>
        private void AddHistoricStateEntry(HistoricStateEntry<TState, object> HistoricStateEntry)
        {
            if (_options.MaxHistoricalRecords == null)
            {
                return;
            }

            if (_past.Count >= _options.MaxHistoricalRecords + 1)
            {
                _past.RemoveAt(0);
            }
            _past.Add(HistoricStateEntry);
        }

        /// <summary>
        /// Revert state to a previous one.
        /// </summary>
        /// <param name="steps">The number of states (or actions dispatched) since the desired state.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when there are not enough steps recorded to rewind as far as requested.</exception>
        public void Rewind(int steps)
        {
            // rewind 1 more than steps to account for current state being first in _past
            if (_past.Count < steps + 2)
            {
                throw new ArgumentOutOfRangeException(nameof(steps), "Cannot rewind more steps than are recorded.");
            }
            else
            {
                foreach (int step in Enumerable.Range(1, steps + 1))
                {
                    _future.Add(_past.ElementAt(_past.Count - 1));
                    _past.RemoveAt(_past.Count - 1);
                }

                lock (_syncRoot)
                {
                    State = _past.ElementAt(_past.Count - 1).State;
                }

                InvokeOnStateChanged(null);
            }
        }

        /// <summary>
        /// Return to a state that has been reverted.
        /// </summary>
        /// <param name="steps">The number of states (or actions) to replay.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when there are not enough steps reverted to fastforward as far as requested.</exception>
        public void FastForward(int steps)
        {
            if (_future.Count < steps)
            {
                throw new ArgumentOutOfRangeException(nameof(steps), "Cannot fast forward more steps than are recorded (dispatching an action erases possible futures).");
            }
            else
            {
                foreach (int step in Enumerable.Range(1, steps))
                {
                    AddHistoricStateEntry(_future.ElementAt(_future.Count - 1));
                    _future.RemoveAt(_future.Count - 1);
                }

                lock (_syncRoot)
                {
                    State = _past.ElementAt(_past.Count - 1).State;
                }

                InvokeOnStateChanged(null);
            }
        }
        #endregion

        #region Location Management
        /// <summary>
        /// Adds location synchronization to the store. When the URI changes, a location-changing action will be dispatched.
        /// </summary>
        /// <param name="uriHelper">The application's UriHelper.</param>
        internal void InitializeLocationSynchronization(IUriHelper uriHelper)
        {
            if (_uriHelper == null && uriHelper != null && !_options.SuppressLocationSynchronization)
            {
                lock (_syncRoot)
                {
                    _uriHelper = uriHelper;
                    _uriHelper.OnLocationChanged += SynchronizeStateLocationWithUri;

                    _locationActionCreator = _options.LocationActionCreator ?? LocationActionCreatorFallback;
                }

                SynchronizeStateLocationWithUri(null, new LocationChangedEventArgs(_uriHelper.GetAbsoluteUri(), false));
            }
        }

        private TAction LocationActionCreatorFallback(string uri) => (TAction)(object)new ChangeLocationAction(uri);

        private void SynchronizeStateLocationWithUri(object sender, LocationChangedEventArgs e)
        {
            if (e.Location != _currentLocation && _locationActionCreator != null)
            {
                lock (_syncRoot)
                {
                    _currentLocation = e.Location;
                }

                Dispatch(_locationActionCreator(e.Location));
            }
        }
        #endregion

        #region IDisposable Support
        private bool disposedValue = false;  // to detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // free managed resources
                    if (_uriHelper != null)
                    {
                        _uriHelper.OnLocationChanged -= SynchronizeStateLocationWithUri;
                    }
                }

                // free unmanaged resources (unmanaged objects)
                // set large fields to null

                disposedValue = true;
            }
        }

        // override a finalizer if Dispose(bool disposing) above has code to free unmanaged resources
        // ~Store()
        // {
        //   // do not change this code; put cleanup code in Dispose(bool disposing) above
        //   Dispose(false);
        // }

        void IDisposable.Dispose()
        {
            // do not change this code; put cleanup code in Dispose(bool disposing) above
            Dispose(true);
            // uncomment the following line if the finalizer is overridden above
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
