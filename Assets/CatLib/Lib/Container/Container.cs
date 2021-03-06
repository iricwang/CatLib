﻿/*
 * This file is part of the CatLib package.
 *
 * (c) Yu Bin <support@catlib.io>
 *
 * For the full copyright and license information, please view the LICENSE
 * file that was distributed with this source code.
 *
 * Document: http://catlib.io/
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using CatLib.API;
using CatLib.API.Container;

namespace CatLib.Container
{
    ///<summary>容器</summary>
    public class Container : IContainer
    {

        /// <summary>
        /// 绑定数据
        /// </summary>
        private Dictionary<string, BindData> binds;

        ///<summary>
        /// 静态化内容
        ///</summary>
        private Dictionary<string, object> instances;

        ///<summary>
        /// 别名(key: 别名 , value: 服务名)
        ///</summary>
        private Dictionary<string, string> alias;

        /// <summary>
        /// 标记
        /// </summary>
        private Dictionary<string, List<string>> tags;

        ///<summary>
        /// 类型字典
        ///</summary>
        private Dictionary<string, Type> typeDict;

        /// <summary>
        /// 修饰器
        /// </summary>
        private List<Func<IBindData, object, object>> decorator;

        /// <summary>
        /// locker
        /// </summary>
        private object locker = new object();

        /// <summary>
        /// AOP代理
        /// </summary>
        private IBoundProxy proxy;

        /// <summary>
        /// 构造一个容器
        /// </summary>
        public Container() { Initialize(); }

        /// <summary>
        /// 为一个及以上的服务定义一个标记
        /// </summary>
        /// <param name="tag">标记名</param>
        /// <param name="service">服务名</param>
        public void Tag(string tag, params string[] service)
        {
            if (service == null) { return; }
            if (service.Length <= 0) { return; }
            if (!tags.ContainsKey(tag)) { tags.Add(tag, new List<string>()); }
            tags[tag].AddRange(service);
        }

        /// <summary>
        /// 根据标记名生成对应的所有服务
        /// </summary>
        /// <param name="tag">标记名</param>
        /// <returns></returns>
        public object[] Tagged(string tag)
        {
            if (!tags.ContainsKey(tag)) { return new object[] { }; }

            List<object> result = new List<object>();

            for (int i = 0; i < tags[tag].Count; ++i)
            {
                result.Add(Make(tags[tag][i]));
            }

            return result.ToArray();
        }

        /// <summary>
        /// 获取服务的绑定数据(如果绑定不存在则返回null)
        /// </summary>
        /// <param name="service">服务名</param>
        /// <returns></returns>
        public IBindData GetBind(string service)
        {
            service = Normalize(service);
            service = GetAlias(service);

            if (binds.ContainsKey(service))
            {
                return binds[service];
            }

            return null;
        }

        /// <summary>
        /// 是否已经绑定了给定名字的服务
        /// </summary>
        /// <param name="service">服务名</param>
        /// <returns></returns>
        public bool HasBind(string service)
        {
            service = Normalize(service);
            if (binds.ContainsKey(service) || alias.ContainsKey(service))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 给定的服务是否是一个静态服务
        /// </summary>
        /// <param name="service">服务名</param>
        /// <returns></returns>
        public bool IsStatic(string service)
        {
            if (!HasBind(service)) { return false; }

            service = Normalize(service);
            service = GetAlias(service);
            return binds[service].IsStatic;
        }

        /// <summary>
        /// 设定一个别名
        /// </summary>
        /// <param name="alias">别名</param>
        /// <param name="service">指向的服务</param>
        /// <returns></returns>
        public IContainer Alias(string alias, string service)
        {
            lock (locker)
            {
                alias = Normalize(alias);
                service = Normalize(service);
                if (this.alias.ContainsKey(alias))
                {

                    // 覆盖别名是一个非常危险的操作，这会导致未知的注入，所以我们直接抛出一个异常。
                    throw new CatLibException("alias [" + alias + "] is already exists!");
                    //this.alias.Remove(alias);
                }

                this.alias.Add(alias, service);
            }
            return this;
        }

        /// <summary>
        /// 如果服务不存在那么绑定
        /// </summary>
        /// <param name="service">服务名</param>
        /// <param name="concrete">服务实体</param>
        /// <param name="isStatic">服务是否是静态的</param>
        /// <returns></returns>
        public IBindData BindIf(string service, Func<IContainer, object[], object> concrete, bool isStatic)
        {
            var bind = GetBind(service);
            if (bind != null)
            {
                return bind;
            }
            return Bind(service, concrete, isStatic);
        }

        /// <summary>
        /// 如果服务不存在那么绑定
        /// </summary>
        /// <param name="service">服务名</param>
        /// <param name="concrete">服务实体</param>
        /// <param name="isStatic">服务是否是静态的</param>
        /// <returns></returns>
        public IBindData BindIf(string service, string concrete, bool isStatic)
        {
            var bind = GetBind(service);
            if (bind != null)
            {
                return bind;
            }
            return Bind(service, concrete, isStatic);
        }

        /// <summary>
        /// 绑定一个服务
        /// </summary>
        /// <param name="service">服务名</param>
        /// <param name="concrete">服务实体</param>
        /// <param name="isStatic">服务是否静态化</param>
        /// <returns></returns>
        public IBindData Bind(string service, string concrete, bool isStatic)
        {
            service = Normalize(service);
            concrete = Normalize(concrete);
            return Bind(service, (c, param) =>
            {
                Container container = c as Container;
                return container.NormalMake(concrete, false, param);
            }, isStatic);
        }

        /// <summary>
        /// 绑定一个服务
        /// </summary>
        /// <param name="service">服务名</param>
        /// <param name="concrete">服务实体</param>
        /// <param name="isStatic">服务是否静态化</param>
        /// <returns></returns>
        public IBindData Bind(string service, Func<IContainer, object[], object> concrete, bool isStatic)
        {
            lock (locker)
            {
                service = Normalize(service);

                instances.Remove(service);
                alias = alias.RemoveValue(service);
                alias.Remove(service);

                BindData bindData = new BindData(this, service, concrete, isStatic);

                if (binds.ContainsKey(service))
                {
                    throw new CatLibException("bind service [" + service + "] is already exists!");
                }

                binds.Add(service, bindData);

                return bindData;
            }
        }

        /// <summary>
        /// 以依赖注入形式调用一个方法
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="method"></param>
        /// <param name="param"></param>
        public object Call(object instance, string method, params object[] param)
        {

            if (instance == null)
            {
                throw new RuntimeException("call instance is null");
            }

            MethodInfo methodInfo = instance.GetType().GetMethod(method);

            return Call(instance, methodInfo, param);

        }

        /// <summary>
        /// 以依赖注入形式调用一个方法
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="method"></param>
        /// <param name="param"></param>
        public object Call(object instance, MethodInfo methodInfo, params object[] param)
        {

            if (instance == null)
            {
                throw new RuntimeException("call instance is null");
            }

            Type type = instance.GetType();

            if (methodInfo == null) { throw new RuntimeException("can not find instance [" + type.ToString() + "] 's function :" + methodInfo.Name); }

            List<ParameterInfo> parameter = new List<ParameterInfo>(methodInfo.GetParameters());

            var bindData = GetBindData(type.ToString());
            if (parameter.Count > 0)
            {
                param = GetDependencies(bindData, type, parameter, param);
            }
            else
            {
                param = new object[] { };
            }

            return methodInfo.Invoke(instance, param);

        }

        /// <summary>
        /// 构造服务
        /// </summary>
        /// <param name="service">服务名或别名</param>
        /// <param name="param">附带参数</param>
        /// <returns></returns>
        public object Make(string service, params object[] param)
        {
            lock (locker)
            {
                service = Normalize(service);
                service = GetAlias(service);
                return NormalMake(service, true, param);
            }
        }

        /// <summary>
        /// 构造服务
        /// </summary>
        public object this[string service] { get { return Make(service); } }

        /// <summary>
        /// 构造服务
        /// </summary>
        public object this[Type service] { get { return Make(service.ToString()); } }

        /// <summary>添加到静态内容</summary>
        /// <param name="type">类型</param>
        /// <param name="alias">别名</param>
        /// <param name="objectData">实体数据</param>
        public void Instance(string service, object objectData)
        {
            lock (locker)
            {
                if (objectData == null)
                {
                    instances.Remove(service);
                    return;
                }

                service = Normalize(service);
                service = GetAlias(service);

                if (instances.ContainsKey(service))
                {
                    instances.Remove(service);
                }

                instances.Add(service, objectData);
            }
        }

        /// <summary>
        /// 当解决类型时触发的事件
        /// </summary>
        /// <param name="func"></param>
        public IContainer OnResolving(Func<IBindData, object, object> func)
        {
            lock (locker)
            {
                if (decorator == null) { decorator = new List<Func<IBindData, object, object>>(); }
                decorator.Add(func);
                foreach (KeyValuePair<string, object> data in instances)
                {
                    var bindData = GetBindData(data.Key);
                    instances[data.Key] = func(bindData, data.Value);
                }
                return this;
            }
        }

        /// <summary>
        /// 执行全局修饰器
        /// </summary>
        /// <param name="bindData"></param>
        /// <param name="obj"></param>
        /// <returns></returns>
        private object ExecDecorator(BindData bindData, object obj)
        {
            if (decorator != null)
            {
                foreach (Func<IBindData, object, object> func in decorator)
                {
                    obj = func(bindData, obj);
                }
            }
            return obj;
        }

        /// <summary>
        /// 构造服务
        /// </summary>
        /// <param name="service">服务名</param>
        /// <param name="isFromMake">是否直接调用自Make函数</param>
        /// <param name="param">参数</param>
        /// <returns></returns>
        private object NormalMake(string service, bool isFromMake, params object[] param)
        {
            if (instances.ContainsKey(service)) { return instances[service]; }

            var bindData = GetBindData(service);
            object objectData = isFromMake ? NormalBuild(bindData, param) : Build(bindData, service, param);

            //只有是来自于make函数的调用时才执行di
            if (isFromMake /*|| (isFromMake && bindData.Concrete != null)*/) //如果是以闭包形式的bind 那么被屏蔽的语句会导致2次di 但我们依旧先不急着去除等一段时间后再删除
            {

                DIAttr(bindData, objectData);

                if (proxy != null) { objectData = proxy.Bound(objectData, bindData); }

                objectData = ExecDecorator(bindData, bindData.ExecDecorator(objectData));

                if (bindData.IsStatic)
                {
                    Instance(service, objectData);
                }
            }

            return objectData;
        }

        /// <summary>
        /// 常规编译
        /// </summary>
        /// <param name="bindData"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        private object NormalBuild(BindData bindData, object[] param)
        {
            if (bindData.Concrete != null)
            {
                return bindData.Concrete(this, param);
            }
            return NormalMake(bindData.Service, false, param); //Build(bindData , bindData.Service, param); 这句语句之前导致了没有正确给定注入实体。但是我们先保留一段时间后再删除
        }

        /// <summary>构造服务</summary>
        /// <param name="type"></param
        /// <param name="bindData"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        private object Build(BindData bindData, string service, object[] param)
        {
            if (param == null) { param = new object[] { }; }
            Type type = GetType(bindData.Service);
            if (type == null) { return null; }

            if (type.IsAbstract || type.IsInterface)
            {
                if (service != bindData.Service)
                {
                    type = Type.GetType(service);
                }
                else
                {
                    return null;
                }
            }
            ConstructorInfo[] constructor = type.GetConstructors();
            if (constructor.Length <= 0)
            {
                return Activator.CreateInstance(type);
            }

            List<ParameterInfo> parameter = new List<ParameterInfo>(constructor[constructor.Length - 1].GetParameters());
            parameter.RemoveRange(0, param.Length);

            if (parameter.Count > 0) { param = GetDependencies(bindData, type, parameter, param); }

            return Activator.CreateInstance(type, param);
        }

        /// <summary>标准化服务名</summary>
        /// <param name="service">服务名</param>
        /// <returns></returns>
        private string Normalize(string service)
        {
            return service.Trim();
        }

        /// <summary>属性注入</summary>
        /// <param name="cls"></param>
        private void DIAttr(BindData bindData, object cls)
        {
            if (cls == null) { return; }

            string typeName;
            foreach (PropertyInfo property in cls.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {

                if (!property.CanWrite) { continue; }
                object[] propertyAttrs = property.GetCustomAttributes(typeof(DependencyAttribute), true);
                if (propertyAttrs.Length <= 0) { continue; }

                DependencyAttribute dependency = propertyAttrs[0] as DependencyAttribute;
                if (string.IsNullOrEmpty(dependency.Alias))
                {
                    typeName = property.PropertyType.ToString();
                }
                else
                {
                    typeName = dependency.Alias;
                }

                if (property.PropertyType.IsClass || property.PropertyType.IsInterface)
                {
                    property.SetValue(cls, ResloveClassAttr(bindData, cls.GetType(), typeName), null);
                }
                else
                {
                    property.SetValue(cls, ResolveNonClassAttr(bindData, cls.GetType(), typeName), null);
                }
            }
        }

        /// <summary>解决非类类型</summary>
        /// <param name="type">参数类型</param>
        /// <param name="alias">别名</param>
        /// <returns></returns>
        private object ResolveNonClassAttr(BindData bindData, Type parent, string cls)
        {
            return null;
        }

        /// <summary>解决类类型</summary>
        /// <returns></returns>
        private object ResloveClassAttr(BindData bindData, Type parent, string cls)
        {
            return Make(bindData.GetContextual(cls)); ;
        }

        /// <summary>获取依赖关系</summary>
        /// <param name="type">类型</param>
        /// <param name="paramInfo">参数信息</param>
        /// <param name="param">手动输入的参数</param>
        /// <returns></returns>
        private object[] GetDependencies(BindData bindData, Type type, List<ParameterInfo> paramInfo, object[] param)
        {
            List<object> myParam = new List<object>();

            ParameterInfo info;
            for (int i = 0; i < paramInfo.Count; i++)
            {
                info = paramInfo[i];
                if (param != null && i < param.Length)
                {
                    if (param[i] == null || info.ParameterType.IsAssignableFrom(param[i].GetType()))
                    {
                        myParam.Add(param[i]);
                        continue;
                    }
                }

                if (info.ParameterType.IsClass || info.ParameterType.IsInterface)
                {
                    myParam.Add(ResloveClass(bindData, type, info));
                }
                else
                {
                    myParam.Add(ResolveNonClass(bindData, type, info));
                }
            }

            return myParam.ToArray();
        }

        /// <summary>解决非类类型</summary>
        /// <param name="info">参数信息</param>
        /// <returns></returns>
        private object ResolveNonClass(BindData bindData, Type parent, ParameterInfo info)
        {
            return info.DefaultValue;
        }

        /// <summary>解决类类型</summary>
        /// <param name="bindData"></param>
        /// <param name="parent"></param>
        /// <param name="info">参数信息</param>
        /// <returns></returns>
        private object ResloveClass(BindData bindData, Type parent, ParameterInfo info)
        {
            return Make(bindData.GetContextual(info.ParameterType.ToString()), null);
        }

        /// <summary>
        /// 获取别名最终对应的服务名
        /// </summary>
        /// <param name="name">服务名或别名</param>
        /// <returns></returns>
        private string GetAlias(string service)
        {
            if (alias.ContainsKey(service))
            {
                return alias[service];
            }
            return service;
        }

        /// <summary>获取服务绑定数据</summary>
        /// <param name="service">服务名</param>
        /// <returns></returns>
        private BindData GetBindData(string service)
        {
            if (!binds.ContainsKey(service))
            {
                return new BindData(this, service, null, false);
            }
            return binds[service];
        }

        /// <summary>获取类型映射</summary>
        /// <param name="service">服务名</param>
        /// <returns></returns>
        private Type GetType(string service)
        {
            if (typeDict.ContainsKey(service))
            {

                return typeDict[service];

            }

            return Type.GetType(service);
        }

        /// <summary>
        /// 初始化
        /// </summary>
        private void Initialize()
        {
            tags = new Dictionary<string, List<string>>();
            alias = new Dictionary<string, string>();
            typeDict = new Dictionary<string, Type>();
            instances = new Dictionary<string, object>();
            binds = new Dictionary<string, BindData>();
            decorator = new List<Func<IBindData, object, object>>();
            proxy = new BoundProxy();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (!typeDict.ContainsKey(type.ToString()))
                    {
                        typeDict.Add(type.ToString(), type);
                    }
                }
            }
        }

    }
}