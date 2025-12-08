using Content.Server.Administration.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Maps;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server._CorvaxGoob.Deathmatch_CS;

public sealed class Session
{
    public MapId MapId;
    public List<EntityUid> Players = new();
}

public sealed class CSRuleSystem : GameRuleSystem<CSRuleComponent>
{
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly IGameMapManager _gameMapManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;

    private List<Session> _sessions = new();
    public List<Session> PSessions => _sessions;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GameRunLevelChangedEvent>(MapClearing);
        SubscribeLocalEvent<GameRuleStartedEvent>(RoundStart);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(RoundEnd);

        SubscribeLocalEvent<IsFighterComponent, MobStateChangedEvent>(OnKillReported);
        SubscribeLocalEvent<IsFighterComponent, PlayerDetachedEvent>(PlayerHasDisconnectednected);
        SubscribeLocalEvent<IsFighterComponent, EraseEvent>(EraseАPlayer);
        SubscribeLocalEvent<IsFighterComponent, EntityTerminatingEvent>(DeleteАPlayer);
    }

    private void RoundStart(ref GameRuleStartedEvent _)
    {
        NewSession();
    }
    private void RoundEnd(RoundRestartCleanupEvent _)
    {
        _sessions.Clear();
    }

    public void NewSession()
    {
        var query = EntityQueryEnumerator<CSRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uId, out var csRuleC, out var gRuleC))
        {
            if (!GameTicker.IsGameRuleActive(uId, gRuleC))
                return;

            if ((this._sessions?.Count ?? 0) < csRuleC.NumberOfSessions)
            {
                Session newsession = new();
                GameMapPrototype? protoMap;

                if (!csRuleC.RandomArena)
                    protoMap = _gameMapManager.GetSelectedMap();
                else
                {
                    var maps = _gameMapManager.CurrentlyEligibleMaps().ToList();
                    protoMap = _random.Pick(maps);
                }

                Addmap(out newsession.MapId, protoMap);
                _sessions?.Add(newsession);
            }
        }
    }
    private void Addmap(out MapId mapId, GameMapPrototype? mapProto)
    {
        if (mapProto == null)
        {
            _gameMapManager.SelectMapByConfigRules();
            mapProto = _gameMapManager.GetSelectedMap();
        }
        GameTicker.LoadGameMap(mapProto!, out MapId mapIdproxy);
        mapId = mapIdproxy;
        _map.InitializeMap(mapId);
    }

    private void MapClearing(GameRunLevelChangedEvent ev)
    {
        var query = EntityQueryEnumerator<CSRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uId, out _, out var gRuleC))
        {
            if (!GameTicker.IsGameRuleActive(uId, gRuleC))
                return;

            var activeMapIds = new HashSet<MapId>(_sessions.Select(s => s.MapId));
            foreach (var mapId in _map.GetAllMapIds())
            {
                if (_map.MapExists(mapId) && !activeMapIds.Contains(mapId))
                {
                    _map.DeleteMap(mapId);
                }
            }
        }
    }

    private void OnKillReported(EntityUid uid, IsFighterComponent _, MobStateChangedEvent args)
    {
        if (MobState.Dead != args.NewMobState || _sessions == null) return;
        RemovingRromSession(uid);
    }
    private void PlayerHasDisconnectednected(EntityUid uid, IsFighterComponent _, PlayerDetachedEvent args)
    {
        RemovingRromSession(uid);
    }
    private void EraseАPlayer(EntityUid uid, IsFighterComponent _, EraseEvent args)
    {
        RemovingRromSession(uid);
    }
    private void DeleteАPlayer(EntityUid uid, IsFighterComponent _, EntityTerminatingEvent args)
    {
        RemovingRromSession(uid);
    }
    private void RemovingRromSession(EntityUid uid)
    {
        foreach (var session in _sessions)
        {
            if (!session.Players.Contains(uid)) continue;
            session.Players.Remove(uid);

            if (session.Players.Count == 0)
            {
                var query2 = EntityQueryEnumerator<IsFighterComponent, GhostRoleComponent, TransformComponent>();
                int count = 0;
                while (query2.MoveNext(out var guid, out _, out _, out var xform))
                {
                    if (xform.MapID == session.MapId)
                        if (EntityManager.TryGetComponent(guid, out MindContainerComponent? mindContC))
                            if (mindContC.Mind == null && _mobStateSystem.IsAlive(guid)) count++;
                }
                if (count == 0)
                {
                    _map.DeleteMap(session.MapId);
                    _sessions.Remove(session);
                    NewSession();
                }
            }
            break;
        }
    }
    // ТуДу
    // разделение на команды
    // проверка связности айди
}
