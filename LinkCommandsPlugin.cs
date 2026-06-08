using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenMod.API;
using OpenMod.API.Commands;
using OpenMod.API.Permissions;
using OpenMod.API.Plugins;
using OpenMod.API.Prioritization;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Plugins;
using OpenMod.Unturned.Users;
using System.Reflection;

[assembly: PluginMetadata(
    "LinkCommands.OpenMod",
    DisplayName = "LinkCommands",
    Author = "LinkCommands")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace LinkCommands.OpenMod
{
    public sealed class LinkCommandsOpenModPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration configuration;
        private readonly CommandStoreOptions commandStoreOptions;
        private readonly IPermissionRegistry permissionRegistry;
        private LinkCommandSource commandSource;

        public LinkCommandsOpenModPlugin(
            IConfiguration configuration,
            IOptions<CommandStoreOptions> commandStoreOptions,
            IPermissionRegistry permissionRegistry,
            IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            this.configuration = configuration;
            this.commandStoreOptions = commandStoreOptions.Value;
            this.permissionRegistry = permissionRegistry;
        }

        protected override UniTask OnLoadAsync()
        {
            commandSource = new LinkCommandSource(this, configuration);
            commandStoreOptions.AddCommandSource(commandSource);

            foreach (LinkCommandRegistration registration in commandSource.Commands)
            {
                permissionRegistry.RegisterPermission(
                    this,
                    "commands." + registration.Name,
                    "Allows use of /" + registration.Name + ".",
                    PermissionGrantResult.Grant);
            }

            return UniTask.CompletedTask;
        }

        protected override UniTask OnUnloadAsync()
        {
            if (commandSource != null)
            {
                commandStoreOptions.RemoveCommandSource(commandSource);
                commandSource = null;
            }

            return UniTask.CompletedTask;
        }
    }

    internal sealed class LinkCommandSource : ICommandSource
    {
        public LinkCommandSource(IOpenModComponent component, IConfiguration configuration)
        {
            List<LinkCommandRegistration> commands =
                new List<LinkCommandRegistration>();
            HashSet<string> names =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (IConfigurationSection section
                in configuration.GetSection("links").GetChildren())
            {
                string name = (section["command"] ?? string.Empty)
                    .Trim()
                    .TrimStart('/');
                string message = (section["message"] ?? string.Empty).Trim();
                string url = (section["url"] ?? string.Empty).Trim();

                Uri uri;
                if (name.Length == 0 ||
                    name.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0 ||
                    !names.Add(name) ||
                    !Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp &&
                     uri.Scheme != Uri.UriSchemeHttps))
                {
                    continue;
                }

                commands.Add(new LinkCommandRegistration(
                    component,
                    name,
                    message.Length == 0 ? "Open this link?" : message,
                    uri.AbsoluteUri));
            }

            Commands = commands.AsReadOnly();
        }

        public IReadOnlyCollection<LinkCommandRegistration> Commands
        {
            get;
            private set;
        }

        public Task<IReadOnlyCollection<ICommandRegistration>> GetCommandsAsync()
        {
            IReadOnlyCollection<ICommandRegistration> commands = Commands;
            return Task.FromResult(commands);
        }
    }

    internal sealed class LinkCommandRegistration : ICommandRegistration
    {
        private static readonly IReadOnlyCollection<string> EmptyAliases =
            new List<string>().AsReadOnly();
        private static readonly IReadOnlyCollection<IPermissionRegistration>
            EmptyPermissions =
                new List<IPermissionRegistration>().AsReadOnly();

        public LinkCommandRegistration(
            IOpenModComponent component,
            string name,
            string message,
            string url)
        {
            Component = component;
            Name = name;
            Message = message;
            Url = url;
            Id = "LinkCommands.OpenMod.Commands." + name.ToLowerInvariant();
        }

        public IOpenModComponent Component { get; private set; }
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Message { get; private set; }
        public string Url { get; private set; }
        public IReadOnlyCollection<string> Aliases { get { return EmptyAliases; } }
        public IReadOnlyCollection<IPermissionRegistration> PermissionRegistrations
        {
            get { return EmptyPermissions; }
        }
        public string Description { get { return "Opens a configured web link."; } }
        public string Syntax { get { return string.Empty; } }
        public Priority Priority { get { return Priority.Normal; } }
        public string ParentId { get { return null; } }
        public bool IsEnabled { get { return Component.IsComponentAlive; } }

        public bool SupportsActor(ICommandActor actor)
        {
            return actor is UnturnedUser;
        }

        public ICommand Instantiate(IServiceProvider serviceProvider)
        {
            ICurrentCommandContextAccessor accessor =
                (ICurrentCommandContextAccessor)serviceProvider.GetService(
                    typeof(ICurrentCommandContextAccessor));
            return new LinkCommand(accessor.Context, Message, Url);
        }
    }

    internal sealed class LinkCommand : ICommand
    {
        private readonly ICommandContext context;
        private readonly string message;
        private readonly string url;

        public LinkCommand(ICommandContext context, string message, string url)
        {
            this.context = context;
            this.message = message;
            this.url = url;
        }

        public Task ExecuteAsync()
        {
            UnturnedUser user = context.Actor as UnturnedUser;
            if (user != null && user.Player != null && user.Player.Player != null)
            {
                user.Player.Player.sendBrowserRequest(message, url);
            }

            return Task.CompletedTask;
        }
    }
}
