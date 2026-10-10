using Tsvrc.Core;
using Tsvrc.UdonTestKit;

namespace Tsvrc.Tests.EditMode
{
    // Subscribes NewListener to the event it was called from, for a TsSubscribe call made
    // while TsEmit is still delivering that event.
    public class ReentrantSubscriberSpy : CallbackSpy
    {
        public TsvrcBehaviour Publisher;
        public string EventName;
        public CallbackSpy NewListener;

        public void SubscribeNewListener()
        {
            Record();
            Publisher.TsSubscribe(NewListener, EventName, nameof(CallbackA));
        }
    }
}
