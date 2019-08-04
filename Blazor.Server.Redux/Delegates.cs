using System.Threading.Tasks;

namespace Blazor.Server.Redux
{
    public delegate Task<TState> Reducer<TState, in TAction>(TState previousState, TAction action);
}
