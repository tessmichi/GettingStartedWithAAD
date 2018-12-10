// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

const { HeroCard, BotStateSet, BotFrameworkAdapter, MemoryStorage, ConversationState, UserState, CardFactory } = require('botbuilder');
const restify = require('restify');
const rp = require('request-promise');

// Create server
let server = restify.createServer();
server.listen(process.env.port || process.env.PORT || 3978, function () {
    console.log(`${server.name} listening to ${server.url}`);
});

// Create adapter
const adapter = new BotFrameworkAdapter({
    appId: process.env.MicrosoftAppId,
    appPassword: process.env.MicrosoftAppPassword
});

// Add state middleware
const storage = new MemoryStorage();
const convoState = new ConversationState(storage);
const userState = new UserState(storage);
adapter.use(new BotStateSet(convoState, userState));

//TODO this shoould all be a db and accessed from bot
//const topics = ["version", "services", "permission", "type", "language"];
const topics = ["AAD Application Version", "Kinds of Services Needed", "Permission Type", "Application Type", "Programming Language"];
const multipleAllowed = [false, true, true, false, false];
const choiceOptions = [
    [
        {
            "title": "v1",
            "value": "v1"
        },
        {
            "title": "v2",
            "value": "v2"
        }],
    [
        {
            "title": "graph",
            "value": "graph"
        },
        {
            "title": "v1 services",
            "value": "v1_services"
        },
        {
            "title": "v2 services",
            "value": "v2_services"
        }],
    [
        {
            "title": "user's profile",
            "value": "code_flow,delegated" // TODO undo this hack. should support them separately.
        },
        {
            "title": "app's own profile",
            "value": "app"
        }],
    [
        {
            "title": "web app",
            "value": "web_app"
        },
        {
            "title": "single page app",
            "value": "spa"
        },
        {
            "title": "background service",
            "value": "service"
        },
        {
            "title": "native app",
            "value": "nativeApp"
        }],
    [
        {
            "title": ".net",
            "value": ".net"
        },
        {
            "title": "javascript",
            "value": "javascript"
        },
        {
            "title": "ios",
            "value": "ios"
        },
        {
            "title": "macos",
            "value": "macos"
        },
        {
            "title": "android",
            "value": "android"
        },
        {
            "title": "java",
            "value": "java"
        },
        {
            "title": "python",
            "value": "python"
        }]];
var logicAppUrl = process.env.logicAppUrl;
var websiteBaseUrl = process.env.websiteBaseUrl;

// Listen for incoming requests 
server.post('/api/messages', (req, res) => {
    // Route received request to adapter for processing
    adapter.processActivity(req, res, (context) => {
        if (context.activity.type === 'message') {
            const state = convoState.get(context);

            // set state variables if they're not set
            if (state.currentTopic === undefined || state.currentTopic === null ||
                state.tags === undefined || state.tags === null) {
                state.currentTopic = 0;
                state.tags = "";
            }

            // if they're set, and the message has a value aka from the submit action from a card,
            // then this message was an answer to a question so save it and move on
            else if (context.activity.value) {
                state.currentTopic++;
                state.tags += state.tags.length === 0 ? `${context.activity.value.choice}` : `,${context.activity.value.choice}`;
                //console.log(`\ngot a button response, choice = ${context.activity.value.choice}\n`);
            }

            // if the topic has gone past the list, there aren't any options left so make the api call
            if (state.currentTopic >= topics.length) {
                // Generate the file by calling the logic app
                var options = {
                    method: 'POST',
                    uri: logicAppUrl,
                    body: {
                        selections: state.tags
                    },
                    json: true // Automatically stringifies the body to JSON
                };
                rp(options)
                    .catch(function (err) {

                        // Clear context
                        state.currentTopic = 0;
                        state.tags = "";
                        return context.sendActivity(`error: ${err}`);
                    });

                // The path to the file
                var url = `${websiteBaseUrl}${state.tags}.html`;

                // Clear context
                state.currentTopic = 0;
                state.tags = "";

                // Inform user
                return context.sendActivity({ attachments: [createLinkCard(url), createQuestionCard(state.currentTopic, state.tags)] });
            }

            //return context.sendActivity(`Which ${topics[state.currentTopic]} do you want? Currently you have chosen ${state.tags}`);
            return context.sendActivity({ attachments: [createQuestionCard(state.currentTopic, state.tags)] });
        }
    });
});

function createLinkCard(url) {
    return CardFactory.adaptiveCard(
        {
            "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
            "version": "1.0",
            "type": "AdaptiveCard",
            "body": [
                {
                    "type": "TextBlock",
                    "text": "Here is your personalized documentation.",
                    "weight": "bolder",
                    "size": "medium",
                    "wrap": true
                }
            ],
            "actions": [
                {
                    "type": "Action.OpenUrl",
                    "title": "Documentation",
                    "url": url
                }
            ]
        }
    );
}

function createQuestionCard(currentTopic, currentTags) {
    return CardFactory.adaptiveCard(
        {
            "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
            "version": "1.0",
            "type": "AdaptiveCard",
            "body": [
                {
                    "type": "TextBlock",
                    "text": `Choose an option for your ${topics[currentTopic]}`,
                    "weight": "bolder",
                    "size": "medium",
                    "wrap": true
                },
                {
                    "type": "Input.ChoiceSet",
                    "id": "choice",
                    "isMultiSelect": multipleAllowed[currentTopic],
                    "style": "expanded",
                    "value": "1",
                    "choices": choiceOptions[currentTopic]
                }
            ],
            "actions": [
                {
                    "type": "Action.Submit",
                    "title": "Submit",
                    "data": {}
                }
            ]
        }
    );
}