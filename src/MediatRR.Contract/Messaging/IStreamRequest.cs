namespace MediatRR.Contract.Messaging;

/// <summary>
/// Marker interface to represent a stream request with AsyncEnumerable response
/// </summary>
/// <typeparam name="TResponse">Response type</typeparam>
public interface IStreamRequest<out TResponse>
{
}