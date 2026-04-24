using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

using WitherTorch.Core.Runtime;
using WitherTorch.Core.Servers.Utils;
using WitherTorch.Core.Tagging;

namespace WitherTorch.Core.Servers.Runtime
{
    /// <summary>
    /// 定義一個 Java 執行環境
    /// </summary>
    public partial class JavaRuntimeEnvironment : IRuntimeEnvironment, IPersistentTag, ICloneable
    {
        private const string JavaPathNode = "path";
        private const string JavaPreArgsNode = "pre_args";
        private const string JavaPostArgsNode = "post_args";

        private string? _path, _preArgs, _postArgs;

        private JavaRuntimeEnvironment() { }

        /// <summary>
        /// <see cref="JavaRuntimeEnvironment"/> 的預設建構子
        /// </summary>
        /// <param name="path">執行時所使用的 Java 虛擬機 (java) 位置</param>
        /// <param name="preArgs">執行時所使用的 Java 前置參數 (-jar server.jar 前的參數)</param>
        /// <param name="postArgs">執行時所使用的 Java 後置參數 (-jar server.jar 後的參數)</param>
        public JavaRuntimeEnvironment(string? path = null, string? preArgs = null, string? postArgs = null)
        {
            _path = path;
            _preArgs = preArgs;
            _postArgs = postArgs;
        }

        /// <summary>
        /// 取得或設定執行時所使用的 Java 虛擬機 (java) 位置
        /// </summary>
        public string? JavaPath
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _path;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _path = value;
        }

        /// <summary>
        /// 取得或設定執行時所使用的 Java 前置參數 (-jar server.jar 前的參數)
        /// </summary>
        public string? JavaPreArguments
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _preArgs;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _preArgs = value;
        }

        /// <summary>
        /// 取得或設定執行時所使用的 Java 後置參數 (-jar server.jar 後的參數)
        /// </summary>
        public string? JavaPostArguments
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _postArgs;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _postArgs = value;
        }

        /// <summary>
        /// 傳回一個與目前物件相同的另一個 <see cref="JavaRuntimeEnvironment"/> 副本
        /// </summary>
        public JavaRuntimeEnvironment Clone() => new JavaRuntimeEnvironment(JavaPath, JavaPreArguments, JavaPostArguments);

        /// <summary>
        /// 從 <paramref name="source"/> 載入 Java 執行環境
        /// </summary>
        /// <param name="source">要載入 Java 執行環境的原始 JSON 物件</param>
        /// <returns>是否成功載入</returns>
        public bool Load(JsonObject source)
            => PropertyHelper.TryGetString(source, JavaPathNode, out _path) | 
            PropertyHelper.TryGetString(source, JavaPreArgsNode, out _preArgs) | 
            PropertyHelper.TryGetString(source, JavaPostArgsNode, out _postArgs); // 三條都必須執行，才可讀取到完整的執行環境

        /// <summary>
        /// 將 Java 執行環境儲存至 <paramref name="destination"/> 所指定之物件內
        /// </summary>
        /// <param name="destination">要儲存 Java 執行環境的原始 JSON 物件</param>
        /// <returns>是否成功儲存</returns>
        public bool Store(JsonObject destination)
        {
            destination[JavaPathNode] = JavaPath;
            destination[JavaPreArgsNode] = JavaPreArguments;
            destination[JavaPostArgsNode] = JavaPostArguments;
            return true;
        }

        IPersistentTagFactory IPersistentTag.GetFactory() => _factory;

        object ICloneable.Clone() => Clone();
    }
}
