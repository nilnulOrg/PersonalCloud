﻿using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Utilities;

namespace NSPersonalCloud
{
    /// <summary>
    /// Provides standard handler callbacks for authentication modules.
    /// </summary>
    /// <seealso cref="AuthenticationHandlerCallback"/>
    public static class AuthenticationHandler
    {
        /// <summary>
        /// <para>Unconditionally passes a request down the module chain.</para>
        /// <para>In the case of authentication modules, this is accomplished simply by doing nothing,
        /// as the <see cref="IWebModule.IsFinalHandler">IsFinalHandler</see> property of
        /// authentication modules is always set to <see langword="false"/>.</para>
        /// </summary>
        /// <param name="context">An <see cref="IHttpContext"/> interface representing the context of the request.</param>
        /// <param name="module">The authentication module that called the handler.</param>
        /// <returns>A completed <see cref="Task"/>.</returns>
#pragma warning disable CA1801 // Unused parameter
        public static Task PassThrough(IHttpContext context, AuthenticationModuleBase module)
#pragma warning restore CA1801
            => Task.CompletedTask;

        /// <summary>
        /// <para>Throws a <see cref="HttpException"/> with a response code of <c>401 Unauthorized</c>.</para>
        /// </summary>
        /// <param name="context">An <see cref="IHttpContext"/> interface representing the context of the request.</param>
        /// <param name="module">The authentication module that called the handler.</param>
        /// <returns>This method never returns; it throws an exception instead..</returns>
#pragma warning disable CA1801  // Remove unused parameter
        public static Task Unauthorized(IHttpContext context, AuthenticationModuleBase module)
#pragma warning restore CA1801  // Remove unused parameter
            => throw HttpException.Unauthorized();

        /// <summary>
        /// <para>Throws a <see cref="HttpException"/> with a response code of <c>401 Unauthorized</c>
        /// and, optionally, a custom message and data.</para>
        /// </summary>
        /// <param name="message">A message to include in the response.</param>
        /// <param name="data">The data object to include in the response.</param>
        /// <returns>This method never returns; it throws an exception instead..</returns>
        public static AuthenticationHandlerCallback Unauthorized(string message = null, object data = null)
        {
            return (context, module) => throw HttpException.Unauthorized(message, data);
        }
    }

    /// <summary>
    /// A callback used to handle events in authentication modules.
    /// </summary>
    /// <param name="context">An <see cref="IHttpContext"/> interface representing the context of the request.</param>
    /// <param name="module">The authentication module that called the handler.</param>
    /// <returns>A <see cref="Task"/> representing the ongoing operation.</returns>
    /// <seealso cref="AuthenticationModuleBase.OnInvalidCredentials"/>
    public delegate Task AuthenticationHandlerCallback(IHttpContext context, AuthenticationModuleBase module);

    /// <summary>
    /// Base class for authentication modules.
    /// </summary>
    public abstract class AuthenticationModuleBase : WebModuleBase
    {
        private AuthenticationHandlerCallback _onMissingCredentials = AuthenticationHandler.Unauthorized;
        private AuthenticationHandlerCallback _onInvalidCredentials = AuthenticationHandler.Unauthorized;

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthenticationModuleBase"/> class.
        /// </summary>
        /// <param name="baseRoute">The base route served by this module.</param>
        protected AuthenticationModuleBase(string baseRoute)
            : base(baseRoute)
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// <para>Authentication modules, i.e. modules derived from
        /// <see cref="AuthenticationModuleBase"/>, always have this property
        /// set to <see langword="false"/>.</para>
        /// </remarks>
        public sealed override bool IsFinalHandler => false;

        /// <summary>
        /// <para>Gets or sets an <see cref="AuthenticationHandlerCallback"/> that is called if
        /// authentication could not take place. For example, <see cref="BasicAuthenticationModuleBase"/>
        /// calls this handler if the request has no <c>Authorization</c> header.</para>
        /// <para>The default is
        /// <see cref="AuthenticationHandler.Unauthorized(IHttpContext,AuthenticationModuleBase)">AuthenticationHandler.Unauthorized</see>.</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">The module's configuration is locked.</exception>
        /// <exception cref="ArgumentNullException">This property is being set to <see langword="null"/>.</exception>
        /// <seealso cref="AuthenticateAsync"/>
        /// <seealso cref="OnInvalidCredentials"/>
        /// <seealso cref="AuthenticationHandler"/>
        public AuthenticationHandlerCallback OnMissingCredentials
        {
            get => _onMissingCredentials;
            set {
                EnsureConfigurationNotLocked();
                _onMissingCredentials = Validate.NotNull(nameof(value), value);
            }
        }

        /// <summary>
        /// <para>Gets or sets an <see cref="AuthenticationHandlerCallback"/> that is called if
        /// a request contains invalid credentials.</para>
        /// <para>The default is
        /// <see cref="AuthenticationHandler.Unauthorized(IHttpContext,AuthenticationModuleBase)">AuthenticationHandler.Unauthorized</see>.</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">The module's configuration is locked.</exception>
        /// <exception cref="ArgumentNullException">This property is being set to <see langword="null"/>.</exception>
        /// <seealso cref="AuthenticateAsync"/>
        /// <seealso cref="OnMissingCredentials"/>
        /// <seealso cref="AuthenticationHandler"/>
        public AuthenticationHandlerCallback OnInvalidCredentials
        {
            get => _onInvalidCredentials;
            set {
                EnsureConfigurationNotLocked();
                _onInvalidCredentials = Validate.NotNull(nameof(value), value);
            }
        }

        /// <inheritdoc />
        protected sealed override async Task OnRequestAsync( IHttpContext context)
        {
            if (context==null)
            {
                throw new InvalidOperationException();
            }
            // Skip if an authentication scheme has already been used,
            // regardless of whether the user was authenticated or not.
            // This lets more than one authentication module work in the same server,
            // trying one authentication scheme after another until valid credentials
            // can be established.
            if (context.User.Identity.AuthenticationType.Length > 0)
                return;

            var user = await AuthenticateAsync(context).ConfigureAwait(false);
            context.GetImplementation().User = user;
            if (user.Identity.AuthenticationType.Length == 0)
            {
                await OnMissingCredentials(context, this).ConfigureAwait(false);
            }
            else if (user.Identity.Name.Length == 0)
            {
                await OnInvalidCredentials(context, this).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Asynchronously authenticates a user, based on a request's contents,
        /// yielding an <see cref="IPrincipal"/> interface that will be assigned to
        /// the HTTP context's <see cref="IHttpContext.User">User</see> property
        /// for use by other modules.
        /// </summary>
        /// <param name="context">The HTTP context of the request.</param>
        /// <returns>
        /// <para>A <see cref="Task{TResult}"/> whose result will be as follows:</para>
        /// <list type="table">
        /// <item>
        /// <term>If authentication did not take place (for example, in the case of
        /// <see cref="BasicAuthenticationModuleBase"/>, when the request has no
        /// <c>Authorization</c> header)</term>
        /// <description>An <see cref="IPrincipal"/> interface, whose <see cref="IPrincipal.Identity">Identity</see>
        /// property has both its <see cref="IIdentity.Name">Name</see> and
        /// <see cref="IIdentity.AuthenticationType">AuthenticationType</see> properties set to the
        /// empty string. Returning <see cref="Auth.NoUser"/> is the fastest way in this case.</description>
        /// </item>
        /// <item>
        /// <term>If the request contains INVALID credentials</term>
        /// <description>An <see cref="IPrincipal"/> interface, whose <see cref="IPrincipal.Identity">Identity</see>
        /// property has its <see cref="IIdentity.Name">Name</see> property set to the empty string, and
        /// its <see cref="IIdentity.AuthenticationType">AuthenticationType</see> property set to
        /// a non-empty string. <see cref="Auth.NoUser"/> cannot be returned in this case.</description>
        /// </item>
        /// <item>
        /// <term>If the request contains VALID credentials</term>
        /// <description>An <see cref="IPrincipal"/> interface, whose <see cref="IPrincipal.Identity">Identity</see>
        /// property has both its <see cref="IIdentity.Name">Name</see> and
        /// <see cref="IIdentity.AuthenticationType">AuthenticationType</see> properties set to
        /// non-empty strings.</description>
        /// </item>
        /// </list>
        /// </returns>
        /// <remarks>
        /// <para>this method will not be called if the <see cref="IPrincipal"/> property assigned to the
        /// <see cref="IHttpContext.User">User</see> property of <paramref name="context"/> has an
        /// <see cref="IPrincipal.Identity">Identity</see> with a non-empty
        /// <see cref="IIdentity.AuthenticationType">AuthenticationType</see>.</para>
        /// <para>This way, two or more authentication modules may be added to a <see cref="WebServer"/>.
        /// The first one that succeeds in retrieving user credentials from the request, whether valid or not,
        /// will cause subsequent authentication modules to skip processing the HTTP context completely.</para>
        /// </remarks>
        protected abstract Task<IPrincipal> AuthenticateAsync(IHttpContext context);
    }
}
