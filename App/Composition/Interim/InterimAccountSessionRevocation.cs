using System;
using ProjectZD8.Application.Authentication.Ports;

namespace ProjectZD8ForWindows.AppRoot.Composition.Interim;

/// <summary>
/// 【暫定】ほかの端末へのログアウト通知を行わない代用の境界です。
/// </summary>
/// <remarks>
/// iOS 版の <c>UbiquitousKeyValueStoreAccountSessionRevocationStore</c> の代わり。
/// CloudKit のログアウト通知（PROGRESS.md の設計案）ができたら差し替えます。
/// </remarks>
public sealed class InterimAccountSessionRevocation : IAccountSessionRevocationPort
{
    /// <summary>現在の端末のセッションを登録します（何もしません）。</summary>
    /// <remarks>責務: 通知先がないため何もしません。</remarks>
    /// <param name="userIdentifier">アカウント識別子。</param>
    public void RegisterCurrentSession(string userIdentifier)
    {
    }

    /// <summary>ログアウト通知を出します（何もしません）。</summary>
    /// <remarks>責務: 通知先がないため何もしません。</remarks>
    /// <param name="userIdentifier">アカウント識別子。</param>
    public void PublishRevocation(string userIdentifier)
    {
    }

    /// <summary>通知の監視を始めます（通知は来ません）。</summary>
    /// <remarks>責務: 通知先がないため何もしません。</remarks>
    /// <param name="userIdentifier">アカウント識別子。</param>
    /// <param name="receive">通知を受け取る処理。</param>
    public void StartObserving(string userIdentifier, Action receive)
    {
    }

    /// <summary>監視をやめます。</summary>
    /// <remarks>責務: 通知先がないため何もしません。</remarks>
    public void StopObserving()
    {
    }
}
