using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;


namespace HandsonBot.SampleBot
{
    public class SampleBot : IBot
    {
        private const string WelcomeText = "Welcome to Sample Bot";

        private readonly ILogger _logger;
        private readonly SampleBotAccessors _accessors; // Added
        private readonly DialogSet _dialogs; // Added

        public SampleBot(SampleBotAccessors accessors, ILoggerFactory loggerFactory)
        {
            _accessors = accessors ?? throw new ArgumentException(nameof(accessors));
            _dialogs = new DialogSet(accessors.ConversationDialogState);

            var waterfallSteps = new WaterfallStep[]
            {
        ConfirmAgeStepAsync,
        ExecuteAgeStepAsync,
        ExecuteFinalConfirmStepAsync,
        ExecuteSummaryStepAsync,
            };

            _dialogs.Add(new TextPrompt("name", ValidateHandleNameAsync));
            _dialogs.Add(new ConfirmPrompt("confirm"));
            _dialogs.Add(new NumberPrompt<int>("age"));
            _dialogs.Add(new WaterfallDialog("details", waterfallSteps));

            _logger = loggerFactory.CreateLogger<SampleBot>();
            _logger.LogInformation("Start SampleBot");
        }

        private async Task GetHandleNameAsync(DialogContext dialogContext, DialogTurnResult dialogTurnResult, UserProfile userProfile, CancellationToken cancellationToken)
        {
            if (dialogTurnResult.Status is DialogTurnStatus.Empty)
            {
                await dialogContext.PromptAsync(
                    "name",
                    new PromptOptions
                    {
                        Prompt = MessageFactory.Text("Please tell me your handle name first."),
                        RetryPrompt = MessageFactory.Text("The handle name must be at least 3 words long."),
                    },
                    cancellationToken);
            }

            // If you enter a handle name
            else if (dialogTurnResult.Status is DialogTurnStatus.Complete)
            {
                if (dialogTurnResult.Result != null)
                {
                    // Register your handle name with UserState
                    userProfile.HandleName = (string)dialogTurnResult.Result;
                    await dialogContext.BeginDialogAsync("details", null, cancellationToken); // added
                }
            }
        }

        private Task<bool> ValidateHandleNameAsync(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
        {
            var result = promptContext.Recognized.Value;

            if (result != null && result.Length >= 3)
            {
                var upperValue = result.ToUpperInvariant();
                promptContext.Recognized.Value = upperValue;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (turnContext.Activity.Type == ActivityTypes.Message)
            {
                // We will exchange normal messages here.
                await SendMessageActivityAsync(turnContext, cancellationToken); // updated
            }
            else if (turnContext.Activity.Type == ActivityTypes.ConversationUpdate)
            {
                await SendWelcomeMessageAsync(turnContext, cancellationToken);
            }
            else
            {
                _logger.LogInformation($"passed:{turnContext.Activity.Type}");
            }

            await _accessors.ConversationState.SaveChangesAsync(turnContext, false, cancellationToken);
            await _accessors.UserState.SaveChangesAsync(turnContext, false, cancellationToken);
        }

        private static async Task SendWelcomeMessageAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in turnContext.Activity.MembersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(WelcomeText, cancellationToken: cancellationToken);
                }
            }
        }

        public async Task SendMessageActivityAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            var dialogContext = await _dialogs.CreateContextAsync(turnContext, cancellationToken);
            var dialogTurnResult = await dialogContext.ContinueDialogAsync(cancellationToken);

            var userProfile = await _accessors.UserProfile.GetAsync(turnContext, () => new UserProfile(), cancellationToken);

            // If the handle name is not registered in UserState
            if (userProfile.HandleName == null)
            {
                await GetHandleNameAsync(dialogContext, dialogTurnResult, userProfile, cancellationToken);
            }

            // If you have a handle name registered with UserState
            else
            {
                // added
                if (dialogTurnResult.Status == DialogTurnStatus.Empty)
                {
                    await dialogContext.BeginDialogAsync("details", null, cancellationToken);
                }
            }
        }

        //----------------------------------------------
        //Lab

        private async Task<DialogTurnResult> ConfirmAgeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);

            return await stepContext.PromptAsync(
                "confirm",
                new PromptOptions
                {
                    Prompt = MessageFactory.Text($"{userProfile.HandleName} May I ask your age?"),
                    RetryPrompt = MessageFactory.Text("Answer yes or No."),
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> ExecuteAgeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if ((bool)stepContext.Result)
            {
                return await stepContext.PromptAsync(
                    "age",
                    new PromptOptions
                    {
                        Prompt = MessageFactory.Text("What is your age?"),
                        RetryPrompt = MessageFactory.Text("Enter the age in numbers."),
                    },
                    cancellationToken);
            }
            else
            {
                return await stepContext.NextAsync(-1, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> ExecuteFinalConfirmStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);
            userProfile.Age = (int)stepContext.Result;

            var message = GetAgeAcceptedMessage(userProfile);
            await stepContext.Context.SendActivityAsync(message, cancellationToken);

            return await stepContext.PromptAsync(
                "confirm",
                new PromptOptions { Prompt = MessageFactory.Text("Is this the registration information you want?") },
                cancellationToken);
        }

        private static IActivity GetAgeAcceptedMessage(UserProfile userProfile)
        {
            return MessageFactory.Text(userProfile.Age == -1 ? "Age is private, isn't it?" : $"I'm {userProfile.Age} year old.");
        }

        private async Task<DialogTurnResult> ExecuteSummaryStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if ((bool)stepContext.Result)
            {
                var userProfile = await _accessors.UserProfile.GetAsync(stepContext.Context, () => new UserProfile(), cancellationToken);
                var summaryMessages = GetSummaryMessages(userProfile);
                await stepContext.Context.SendActivitiesAsync(summaryMessages, cancellationToken);

                // End of Detail dialog
                return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
            }
            else
            {
                // Redo the Details dialog.
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("I will visit you again."), cancellationToken);
                return await stepContext.ReplaceDialogAsync("details", cancellationToken: cancellationToken);
            }
        }

        private static IActivity[] GetSummaryMessages(UserProfile userProfile)
        {
            IActivity summaryMessage = MessageFactory.Text(userProfile.Age == -1
                ? $"{userProfile.HandleName} Your age is private."
                : $"{userProfile.HandleName} , {userProfile.Age} year old.");
            IActivity thanksMessage = MessageFactory.Text("Thank you for your input.");
            return new[] { summaryMessage, thanksMessage };
        }


        //---------------------------
    }
}
