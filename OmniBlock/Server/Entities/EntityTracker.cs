using OmniBlock.Entities;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;
using OmniBlock.Network.Snapshots;
using OmniBlock.Util.Maths;

namespace OmniBlock.Server.Entities;

public class EntityTracker
{
    private const int LivingTrackingRange = 512;
    /// <summary>
    ///     Scratch for <see cref="broadcastSnapshots" />: this pass's states, grouped by recipient.
    ///     A field rather than a local so the dictionary's buckets survive between ticks; the lists
    ///     inside it do not, and are not worth pooling at one per player per tick.
    /// </summary>
    private readonly Dictionary<ServerPlayerEntity, List<KeyValuePair<int, EntitySnapshotState>>> _snapshotStates = [];

    private readonly int dimensionId;
    private readonly HashSet<EntityTrackerEntry> entries = [];
    private readonly Dictionary<int, EntityTrackerEntry> entriesById = new();
    private int viewDistance;

    private readonly OmniBlockServer world;

    public EntityTracker(OmniBlockServer server, int dimensionId)
    {
        world = server;
        this.dimensionId = dimensionId;
        viewDistance = server.playerManager.getBlockViewDistance();
    }

    public void onEntityAdded(Entity entity)
    {
        if (entity is ServerPlayerEntity player)
        {
            startTracking(entity, 512, 2);

            foreach (var tracker in entries)
            {
                if (tracker.currentTrackedEntity != player)
                {
                    tracker.updateListener(player);
                }
            }
        }
        // Every mob but the player, which was matched above. This was a marker interface each mob
        // class had to remember to implement; being an EntityLiving is the same fact, already true.
        // Whether velocity is sent used to be a class check for the squid, and is now its own
        // declaration — a mob whose motion the server imposes says so in its definition.
        else if (entity is EntityLiving mob and not EntityPlayer)
        {
            // Living entities remain visible throughout received terrain. Simulation distance is
            // enforced independently by EntityManager, so this does not run distant AI/pathing.
            startTracking(entity, LivingTrackingRange, 3, mob.Definition.TracksVelocity);
        }
        // Non-living entities whose tracking parameters are declared rather than matched by class —
        // primed TNT and falling sand so far.
        else if (entity.Type?.Definition is { TrackingRange: > 0 } declared)
        {
            startTracking(entity, declared.TrackingRange, declared.TrackingFrequency, declared.TracksVelocity);
        }
    }

    public void startTracking(Entity entity, int trackedDistance, int tracingFrequency, bool alwaysUpdateVelocity = false)
    {
        var requestedDistance = trackedDistance;
        trackedDistance = Math.Min(trackedDistance, viewDistance);

        if (entriesById.ContainsKey(entity.ID))
        {
            throw new InvalidOperationException("Entity is already tracked!");
        }

        EntityTrackerEntry trackerEntry = new(entity, trackedDistance, tracingFrequency,
            alwaysUpdateVelocity, requestedDistance);
        entries.Add(trackerEntry);
        entriesById[entity.ID] = trackerEntry;
        trackerEntry.updateListeners(world.getWorld(dimensionId).Entities.Players.Cast<ServerPlayerEntity>());
    }

    /// <summary>Updates existing entries when the server terrain distance changes at runtime.</summary>
    public void SetViewDistance(int blocks)
    {
        viewDistance = Math.Max(0, blocks);
        foreach (var entry in entries) entry.SetViewDistance(viewDistance);
    }

    public void onEntityRemoved(Entity entity)
    {
        if (entity is ServerPlayerEntity)
        {
            var playerEntity = (ServerPlayerEntity)entity;

            foreach (var trackerEntry in entries)
            {
                trackerEntry.notifyEntityRemoved(playerEntity);
            }
        }

        if (entriesById.Remove(entity.ID, out var ent))
        {
            entries.Remove(ent);
            ent.notifyEntityRemoved();
        }
    }

    /// <summary>
    ///     This entity's recent position history, or null when nothing tracks it. See
    ///     <see cref="EntityPositionHistory" /> for what reads it.
    /// </summary>
    public EntityPositionHistory? HistoryFor(int entityId) =>
        entriesById.TryGetValue(entityId, out var entry) ? entry.History : null;

    public void tick()
    {
        List<ServerPlayerEntity> players = [];
        var simulationTimeMs = world.SimulationTimeMs;

        foreach (var tracker in entries)
        {
            tracker.notifyNewLocation(world.getWorld(dimensionId).Entities.Players.Cast<ServerPlayerEntity>(), simulationTimeMs);
            if (tracker.newPlayerDataUpdated && tracker.currentTrackedEntity is ServerPlayerEntity player)
            {
                players.Add(player);
            }
        }

        foreach (var player in players)
        {
            foreach (var tracker in entries)
            {
                if (tracker.currentTrackedEntity != player)
                {
                    tracker.updateListener(player);
                }
            }
        }

        broadcastSnapshots();
    }

    /// <summary>
    ///     Turns this pass into one delta-compressed snapshot per protocol-speaking listener.
    ///     <para>
    ///         Runs after the entries have decided what they have to say, and after listener sets
    ///         have settled for the tick, so a player
    ///         who came into range of an entity during this pass gets it in the same snapshot as
    ///         everything else rather than a tick later.
    ///     </para>
    ///     <para>
    ///         The inversion is the reason this is a separate pass. Entries know their listeners and
    ///         a delta is per listener, so the loop that sends packets cannot also build snapshots —
    ///         it would have to encode each entity once per player against a different baseline, in
    ///         an order neither side controls.
    ///     </para>
    /// </summary>
    private void broadcastSnapshots()
    {
        _snapshotStates.Clear();

        foreach (var entry in entries)
        {
            if (!entry.OfferedThisTick)
            {
                continue;
            }

            var entityId = entry.currentTrackedEntity.ID;
            var state = entry.SnapshotState;

            foreach (var listener in entry.listeners)
            {
                if (listener.NetworkHandler is not { WantsCompactPayloads: true })
                {
                    continue;
                }

                if (!_snapshotStates.TryGetValue(listener, out var states))
                {
                    states = [];
                    _snapshotStates[listener] = states;
                }

                states.Add(new KeyValuePair<int, EntitySnapshotState>(entityId, state));
            }
        }

        foreach (var (player, states) in _snapshotStates)
        {
            var snapshot = player.SnapshotStream.Build(states);
            if (snapshot is not null)
            {
                player.NetworkHandler?.SendMessage(snapshot);
            }
        }
    }

    public void sendToListeners(Entity entity, Packet packet)
    {
        if (entriesById.TryGetValue(entity.ID, out var ent))
        {
            ent.sendToListeners(packet);
        }
    }

    public void sendToAround(Entity entity, Packet packet)
    {
        if (entriesById.TryGetValue(entity.ID, out var ent))
        {
            ent.sendToAround(packet);
        }
    }

    public void sendToListeners(Entity entity, Message message)
    {
        if (entriesById.TryGetValue(entity.ID, out var ent))
        {
            ent.sendToListeners(message);
        }
    }

    public void sendToAround(Entity entity, Message message)
    {
        if (entriesById.TryGetValue(entity.ID, out var ent))
        {
            ent.sendToAround(message);
        }
    }

    public void updateListenerForChunk(ServerPlayerEntity player, int chunkX, int chunkZ)
    {
        foreach (var tracker in entries)
        {
            var entity = tracker.currentTrackedEntity;
            if (entity != player
                && !entity.Dead
                && MathHelper.Floor(entity.X / 16.0) == chunkX
                && MathHelper.Floor(entity.Z / 16.0) == chunkZ)
            {
                tracker.updateListener(player);
            }
        }
    }

    public void removeListener(ServerPlayerEntity player)
    {
        foreach (var trackerEntry in entries)
        {
            trackerEntry.removeListener(player);
        }
    }
}
