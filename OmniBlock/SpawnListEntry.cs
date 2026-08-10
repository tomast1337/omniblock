using OmniBlock.Entities;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock;

public record SpawnListEntry(Func<IWorldContext, EntityLiving> Factory);
