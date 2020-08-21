﻿using System;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using Umbraco.Core.Configuration;
using Umbraco.Core.Configuration.Models;
using Umbraco.Core.Events;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Umbraco.Core
{
    /// <summary>
    /// A utility class for sending emails
    /// </summary>
    public class EmailSender : IEmailSender
    {
        // TODO: This should encapsulate a BackgroundTaskRunner with a queue to send these emails!

        private readonly GlobalSettings _globalSettings;
        private readonly bool _enableEvents;

        public EmailSender(IOptionsSnapshot<GlobalSettings> globalSettings) : this(globalSettings, false)
        {
        }

        public EmailSender(IOptionsSnapshot<GlobalSettings> globalSettings, bool enableEvents)
            : this(globalSettings.Value, enableEvents)
        {
        }

        public EmailSender(GlobalSettings globalSettings) : this(globalSettings, false)
        {
        }

        public EmailSender(GlobalSettings globalSettings, bool enableEvents)
        {
            _globalSettings = globalSettings;
            _enableEvents = enableEvents;

            _smtpConfigured = new Lazy<bool>(() => _globalSettings.IsSmtpServerConfigured);
        }

        private readonly Lazy<bool> _smtpConfigured;

        /// <summary>
        /// Sends the message non-async
        /// </summary>
        /// <param name="message"></param>
        public void Send(MailMessage message)
        {
            if (_smtpConfigured.Value == false && _enableEvents)
            {
                OnSendEmail(new SendEmailEventArgs(message));
            }
            else
            {
                using (var client = new SmtpClient())
                {

                    client.Connect(_globalSettings.SmtpSettings.Host, _globalSettings.SmtpSettings.Port);

                    if (!(_globalSettings.SmtpSettings.Username is null &&
                          _globalSettings.SmtpSettings.Password is null))
                    {
                        client.Authenticate(_globalSettings.SmtpSettings.Username, _globalSettings.SmtpSettings.Password);
                    }

                    client.Send(ConstructEmailMessage(message));
                    client.Disconnect(true);
                }
            }
        }

        /// <summary>
        /// Sends the message async
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public async Task SendAsync(MailMessage message)
        {
            if (_smtpConfigured.Value == false && _enableEvents)
            {
                OnSendEmail(new SendEmailEventArgs(message));
            }
            else
            {
                using (var client = new SmtpClient())
                {
                    await client.ConnectAsync(_globalSettings.SmtpSettings.Host, _globalSettings.SmtpSettings.Port);

                    if (!(_globalSettings.SmtpSettings.Username is null &&
                          _globalSettings.SmtpSettings.Password is null))
                    {
                        await client.AuthenticateAsync(_globalSettings.SmtpSettings.Username, _globalSettings.SmtpSettings.Password);
                    }

                    var mailMessage = ConstructEmailMessage(message);
                    if (_globalSettings.SmtpSettings.DeliveryMethod == SmtpDeliveryMethod.Network)
                    {
                        await client.SendAsync(mailMessage);
                    }
                    else
                    {
                        client.Send(mailMessage);
                    }

                    await client.DisconnectAsync(true);
                }
            }
        }

        /// <summary>
        /// Returns true if the application should be able to send a required application email
        /// </summary>
        /// <remarks>
        /// We assume this is possible if either an event handler is registered or an smtp server is configured
        /// </remarks>
        public static bool CanSendRequiredEmail(GlobalSettings globalSettings) => EventHandlerRegistered || globalSettings.IsSmtpServerConfigured;

        /// <summary>
        /// returns true if an event handler has been registered
        /// </summary>
        internal static bool EventHandlerRegistered
        {
            get { return SendEmail != null; }
        }

        /// <summary>
        /// An event that is raised when no smtp server is configured if events are enabled
        /// </summary>
        internal static event EventHandler<SendEmailEventArgs> SendEmail;

        private static void OnSendEmail(SendEmailEventArgs e)
        {
            var handler = SendEmail;
            if (handler != null) handler(null, e);
        }

        private MimeMessage ConstructEmailMessage(MailMessage mailMessage)
        {
            var fromEmail = mailMessage.From?.Address;
            if(string.IsNullOrEmpty(fromEmail))
                fromEmail = _globalSettings.SmtpSettings.From;

            var messageToSend = new MimeMessage
            {
                Subject = mailMessage.Subject,
                From = { new MailboxAddress(fromEmail)},
                Body = new TextPart(mailMessage.IsBodyHtml ? TextFormat.Html : TextFormat.Plain) { Text = mailMessage.Body }
            };
            messageToSend.To.AddRange(mailMessage.To.Select(x=>new MailboxAddress(x.Address)));

            return messageToSend;
        }
    }
}
