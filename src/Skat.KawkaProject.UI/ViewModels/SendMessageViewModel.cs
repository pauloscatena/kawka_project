using System;
using System.Collections.Generic;
using System.Text;

namespace Skat.KawkaProject.UI.ViewModels
{
    public class SendMessageViewModel : ViewModelBase
    {
        public string KafkaServer { get; set; }
        public string TopicName { get; set; }
        public string Message { get; set; }
        public string ReturnMessage { get; set; }
        
    }
}