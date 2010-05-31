﻿//-----------------------------------------------------------------------
// <copyright file="WebAppVerificationCodeBindingElement.cs" company="Andrew Arnott">
//     Copyright (c) Andrew Arnott. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace DotNetOpenAuth.OAuthWrap.ChannelElements {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using Messages;
	using Messaging;
	using Messaging.Bindings;

	/// <summary>
	/// A binding element for OAuth 2.0 authorization servers that create/verify
	/// issued verification codes as part of obtaining access/refresh tokens.
	/// </summary>
	internal class WebAppVerificationCodeBindingElement : AuthServerBindingElementBase {
		private const string VerificationCodeContext = "{VerificationCode}";

		/// <summary>
		/// Initializes a new instance of the <see cref="WebAppVerificationCodeBindingElement"/> class.
		/// </summary>
		internal WebAppVerificationCodeBindingElement() {
		}

		/// <summary>
		/// Gets the protection commonly offered (if any) by this binding element.
		/// </summary>
		/// <value>Always <c>MessageProtections.None</c></value>
		/// <remarks>
		/// This value is used to assist in sorting binding elements in the channel stack.
		/// </remarks>
		public override MessageProtections Protection {
			get { return MessageProtections.None; }
		}

		/// <summary>
		/// Gets the maximum message age from the standard expiration binding element.
		/// </summary>
		private static TimeSpan MaximumMessageAge {
			get { return StandardExpirationBindingElement.MaximumMessageAge; }
		}

		/// <summary>
		/// Prepares a message for sending based on the rules of this channel binding element.
		/// </summary>
		/// <param name="message">The message to prepare for sending.</param>
		/// <returns>
		/// The protections (if any) that this binding element applied to the message.
		/// Null if this binding element did not even apply to this binding element.
		/// </returns>
		/// <remarks>
		/// Implementations that provide message protection must honor the
		/// <see cref="MessagePartAttribute.RequiredProtection"/> properties where applicable.
		/// </remarks>
		public override MessageProtections? ProcessOutgoingMessage(IProtocolMessage message) {
			var response = message as WebAppSuccessResponse;
			if (response != null) {
				var directResponse = (IDirectResponseProtocolMessage)response;
				var request = (WebAppRequest)directResponse.OriginatingRequest;

				var code = new VerificationCode(this.OAuthChannel, request.Callback, request.Scope, response.AuthorizingUsername);
				response.VerificationCode = code.Encode();

				return MessageProtections.None;
			}

			return null;
		}

		/// <summary>
		/// Performs any transformation on an incoming message that may be necessary and/or
		/// validates an incoming message based on the rules of this channel binding element.
		/// </summary>
		/// <param name="message">The incoming message to process.</param>
		/// <returns>
		/// The protections (if any) that this binding element applied to the message.
		/// Null if this binding element did not even apply to this binding element.
		/// </returns>
		/// <exception cref="ProtocolException">
		/// Thrown when the binding element rules indicate that this message is invalid and should
		/// NOT be processed.
		/// </exception>
		/// <remarks>
		/// Implementations that provide message protection must honor the
		/// <see cref="MessagePartAttribute.RequiredProtection"/> properties where applicable.
		/// </remarks>
		public override MessageProtections? ProcessIncomingMessage(IProtocolMessage message) {
			var request = message as WebAppAccessTokenRequest;
			if (request != null) {
				var client = this.AuthorizationServer.GetClient(request.ClientIdentifier);
				ErrorUtilities.VerifyProtocol(string.Equals(client.Secret, request.ClientSecret, StringComparison.Ordinal), Protocol.incorrect_client_credentials);

				var verificationCode = VerificationCode.Decode(this.OAuthChannel, request.VerificationCode);
				verificationCode.VerifyCallback(request.Callback);

				// Has this verification code expired?
				DateTime expirationDate = verificationCode.CreationDateUtc + MaximumMessageAge;
				if (expirationDate < DateTime.UtcNow) {
					throw new ExpiredMessageException(expirationDate, message);
				}

				// Has this verification code already been used to obtain an access/refresh token?
				if (!this.AuthorizationServer.VerificationCodeNonceStore.StoreNonce(VerificationCodeContext, verificationCode.Nonce, verificationCode.CreationDateUtc)) {
					Logger.OpenId.ErrorFormat("Replayed nonce detected ({0} {1}).  Rejecting message.", verificationCode.Nonce, verificationCode.CreationDateUtc);
					throw new ReplayedMessageException(message);
				}

				request.Scope = verificationCode.Scope;

				return MessageProtections.None;
			}

			return null;
		}
	}
}