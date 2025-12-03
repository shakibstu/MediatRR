using MediatRR.Contract.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests
{
    using System.Collections.Concurrent;

    public class MediatorTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMediator _mediator;

        public MediatorTests()
        {
            var services = new ServiceCollection();
            var deadLetters = new ConcurrentQueue<DeadLettersInfo>();
            services.AddMediatRR(cfg => { }, deadLetters);
            services.AddRequestHandler<TestRequest, string, TestRequestHandler>();

            _serviceProvider = services.BuildServiceProvider();
            _mediator = _serviceProvider.GetRequiredService<IMediator>();
        }

        [Fact]
        public async Task Send_ShouldReturnResponse_WhenHandlerExists()
        {
            var request = new TestRequest { Message = "Hello" };
            var response = await _mediator.Send(request);
            Assert.Equal("Hello Handled", response);
        }

        [Fact]
        public async Task Send_ShouldThrowException_WhenNoHandlerExists()
        {
            var request = new UnhandledRequest();
            await Assert.ThrowsAsync<ArgumentException>(() => _mediator.Send(request));
        }

        [Fact]
        public async Task Publish_ShouldNotThrow_WhenNoHandlerExists()
        {
            var notification = new TestNotification { Message = "Event" };
            await _mediator.Publish(notification);
        }
    }

    public class TestRequest : IRequest<string>
    {
        public string Message { get; set; }
    }

    public class TestRequestHandler : IRequestHandler<TestRequest, string>
    {
        public Task<string> Handle(TestRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request.Message + " Handled");
        }
    }

    public class UnhandledRequest : IRequest<string> { }

    public class TestNotification : INotification
    {
        public string Message { get; set; }
    }
}
