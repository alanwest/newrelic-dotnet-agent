using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using NewRelic.Agent.Core.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NewRelic.Agent.Core.JsonConverters
{
	public class JsonArrayConverter : JsonConverter
	{
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			if (value == null)
				return;
			if (serializer == null)
				return;
			if (writer == null)
				return;

			// Write out the type as an array of values ordered by JSON index
			var values = GetJsonMemberValuesOrderedByIndex(value, true);
			serializer.Serialize(writer, values);
		}

		public override object ReadJson(JsonReader reader, Type type, object existingValue, JsonSerializer serializer)
		{
			if (reader == null)
				return null;
			if (type == null)
				return null;
			if (serializer == null)
				return null;
			if (reader.TokenType == JsonToken.Null)
				return null;

			// Load JSON
			var jArray = JArray.Load(reader);
			if(jArray == null)
				throw new JsonSerializationException("Failed to load JSON into JArray");
			
			// Find all JsonArrayIndex members in object
			var jsonInfos = GetJsonMemberInfosOrderedByIndex(type);
			
			// Return an object that represents the deserialized JSON
			return TryParameterizedConstruct(type, jArray, jsonInfos) ?? DefaultConstruct(type, jArray, jsonInfos);
		}

		[CanBeNull]
		private static object TryParameterizedConstruct([NotNull] Type type, [NotNull] IList<JToken> jArray, [NotNull] IList<JsonMemberInfo> orderedJsonMemberInfos)
		{
			// If the source JSON array has a larger number of values than the number of members with JsonArrayIndexAttribute then it is unlikely that we'll be able to find an appropriate constructor
			if (jArray.Count > orderedJsonMemberInfos.Count)
				return null;

			orderedJsonMemberInfos = orderedJsonMemberInfos.Take(jArray.Count).ToList();

			// Try to find a constructor that has the same number and type of parameters as the members 
			var jsonArrayMemberTypes = orderedJsonMemberInfos.Select(info => info.MemberType).ToArray();
			var constructor = type.GetConstructor(jsonArrayMemberTypes);
			if (constructor == null)
				return null;

			// Try to construct an object with the parameterized constructor that we found
			var jArrayValues = orderedJsonMemberInfos.Select(info => MemberJsonInfoToObject(jArray, info)).ToArray();
			return TryConstruct(constructor, jArrayValues);
		}

		[CanBeNull]
		private static object MemberJsonInfoToObject([NotNull] IList<JToken> jArray, [NotNull] JsonMemberInfo info)
		{
			if (info.Index >= jArray.Count)
				return null;

			var jToken = jArray[(int) info.Index];
			if(jToken == null)
				throw new JsonSerializationException(string.Format("No JToken found at index {0}", info.Index));

			return jToken.ToObject(info.MemberType);
		}

		[NotNull]
		private static object DefaultConstruct([NotNull] Type type, [NotNull] IList<JToken> jArray, [NotNull] IList<JsonMemberInfo> orderedJsonMemberInfos)
		{
			// Find a default constructor
			var constructor = type.GetConstructor(Type.EmptyTypes);
			if (constructor == null)
				throw new JsonSerializationException(string.Format("Failed to deserialize object of type {0} -- type does not have public parameterless constructor", type.Name));

			// Invoke the default constructor
			object deserializedObject = TryConstruct(constructor, null);
			if (deserializedObject == null)
				throw new JsonSerializationException(string.Format("Failed to deserialize object of type {0} -- invoking public parameterless constructor returned null", type.Name));

			// Manually set the values of each JSON member
			for (var index = 0; index < jArray.Count; index++)
			{
				TrySetValue(deserializedObject, orderedJsonMemberInfos, jArray, index);
			}

			return deserializedObject;
		}

		[CanBeNull]
		private static object TryConstruct([NotNull] ConstructorInfo constructor, [CanBeNull] object[] parameterValues)
		{
			try
			{
				return constructor.Invoke(parameterValues);
			}
			catch
			{
				return null;
			}
		}

		public override bool CanConvert(Type objectType) { throw new NotImplementedException(); }

		[NotNull]
		private static IList<object> GetJsonMemberValuesOrderedByIndex([NotNull] object value, bool validateJsonProperties)
		{
			// Find all JsonArrayIndex members in object
			var postProcessed = GetPostProcessed(value);
			var jsonInfos = GetJsonMemberInfosOrderedByIndex(postProcessed.GetType(), postProcessed);

			if (validateJsonProperties)
			{
				// Verify type has members with valid JSON indexes
				if (!jsonInfos.Any())
					throw new JsonSerializationException(string.Format("Failed to serialize object of type {0} -- object has no members marked with JsonArrayIndexAttribute", value.GetType()));
				if (jsonInfos.First().Index != 0)
					throw new JsonSerializationException(string.Format("Failed to serialize object of type {0} -- no field or property found for index 0", value.GetType()));
				if (!jsonInfos.IsSequential(info => info.Index))
					throw new JsonSerializationException(string.Format("Failed to serialize object of type {0} -- JsonArrayIndex sequence is missing a value in the middle of the sequence", value.GetType()));
			}

			return jsonInfos.Select(info => info.ExistingValue).ToList();
		}

		[NotNull]
		private static IList<JsonMemberInfo> GetJsonMemberInfosOrderedByIndex([NotNull] Type type, [CanBeNull] object instance = null)
		{
			var propertyJsonInfo = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(property => property != null)
				.Select(property => TryGetJsonMemberInfo(property, instance));
			var fieldJsonInfo = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
				.Where(field => field != null)
				.Select(field => TryGetJsonMemberInfo(field, instance));

			var jsonMemberInfos = propertyJsonInfo
				.Concat(fieldJsonInfo)
				.Where(info => info != null)
				.OrderBy(info => info.Index)
				.ToList();

			var duplicateIndexGroup = jsonMemberInfos.GroupBy(info => info.Index).FirstOrDefault(group => group != null && group.Count() > 1);
			if (duplicateIndexGroup != null)
				throw new JsonSerializationException(string.Format("Failed to read serialization info for object of type {0} -- index {1} is specified multiple times", type.Name, duplicateIndexGroup.First().Index));

			return jsonMemberInfos;
		}
		
		[NotNull]
		private static object GetPostProcessed([NotNull] object value)
		{
			var properties = value.GetType().GetProperties();
			foreach (var property in properties)
			{
				if (property == null)
					continue;
				if (!Attribute.IsDefined(property, typeof (SerializationStandInAttribute)))
					continue;

				var postProcessed = property.GetValue(value, null);
				if (postProcessed == null)
					continue;

				return postProcessed;
			}

			return value;
		}

		public static object SwapInDateTimeAsUnixTimeSeconds(MemberInfo member, object value)
		{
			var dateTime = (DateTime)value;
			return dateTime.ToUnixTimeSeconds();
		}

		public static object SwapInDateTimeAsUnixTimeMilliseconds(MemberInfo member, object value)
		{
			var dateTime = (DateTime)value;
			return dateTime.ToUnixTimeMilliseconds();
		}

		public static object SwapInTimeSpanAsMilliseconds(MemberInfo member, object value)
		{
			var timeSpan = (TimeSpan)value;
			return timeSpan.TotalMilliseconds;
		}

		public static object SwapInTimeSpanAsSeconds(MemberInfo member, object value)
		{
			var timeSpan = (TimeSpan)value;
			return timeSpan.TotalSeconds;
		}

		[CanBeNull]
		private static JsonMemberInfo TryGetJsonMemberInfo([CanBeNull] FieldInfo fieldInfo, [CanBeNull] object instance)
		{
			if (fieldInfo == null)
				return null;

			var valueGetter = instance == null ? (Func<object>)null : () => fieldInfo.GetValue(instance);
			Action<object, object> valueSetter = fieldInfo.SetValue;

			return TryGetJsonMemberInfo(fieldInfo, valueGetter, fieldInfo.FieldType, valueSetter);
		}

		[CanBeNull]
		private static JsonMemberInfo TryGetJsonMemberInfo([CanBeNull] PropertyInfo propertyInfo, [CanBeNull] object instance)
		{
			if (propertyInfo == null)
				return null;

			var valueGetter = instance == null ? (Func<object>)null : () => propertyInfo.GetValue(instance, null);
			Action<object, object> valueSetter = (targetObject, value) => propertyInfo.SetValue(targetObject, value, null);

			return TryGetJsonMemberInfo(propertyInfo, valueGetter, propertyInfo.PropertyType, valueSetter);
		}

		[CanBeNull]
		private static JsonMemberInfo TryGetJsonMemberInfo([CanBeNull] MemberInfo memberInfo, [CanBeNull] Func<object> getValue, [CanBeNull] Type type, [CanBeNull] Action<object, object> valueSetter)
		{
			if (memberInfo == null)
				return null;
			if (type == null)
				return null;
			if (valueSetter == null)
				return null;

			if (!Attribute.IsDefined(memberInfo, typeof (JsonArrayIndexAttribute)))
				return null;

			var attributes = memberInfo.GetCustomAttributes(typeof (JsonArrayIndexAttribute), true);
			if (attributes.Length <= 0)
				return null;

			if (!(attributes[0] is JsonArrayIndexAttribute attribute))
				return null;

			var index = attribute.Index;
			var existingValue = getValue == null ? null : getValue();

			if (existingValue is DateTime)
			{
				if (Attribute.IsDefined(memberInfo, typeof(DateTimeSerializesAsUnixTimeSecondsAttribute)))
				{
					existingValue = SwapInDateTimeAsUnixTimeSeconds(memberInfo, existingValue);
				}
				else if (Attribute.IsDefined(memberInfo, typeof(DateTimeSerializesAsUnixTimeMillisecondsAttribute)))
				{
					existingValue = SwapInDateTimeAsUnixTimeMilliseconds(memberInfo, existingValue);
				}
			}
			else if (existingValue is TimeSpan)
			{
				if (Attribute.IsDefined(memberInfo, typeof(TimeSpanSerializesAsMillisecondsAttribute)))
				{
					existingValue = SwapInTimeSpanAsMilliseconds(memberInfo, existingValue);
				}
				else if (Attribute.IsDefined(memberInfo, typeof(TimeSpanSerializesAsSecondsAttribute)))
				{
					existingValue = SwapInTimeSpanAsSeconds(memberInfo, existingValue);
				}
			}

			return new JsonMemberInfo(index, existingValue, memberInfo, type, valueSetter);
		}

		private static void TrySetValue([CanBeNull] object targetObject, [CanBeNull] IEnumerable<JsonMemberInfo> jsonInfos, [CanBeNull] IList<JToken> jTokens, int tokenIndex)
		{
			if (targetObject == null)
				return;
			if (jsonInfos == null)
				return;
			if (jTokens == null)
				return;

			// If a member isn't found with a matching JsonArrayIndexAttribute then just skip the token
			var relevantJsonMemberInfo = jsonInfos.SingleOrDefault(info => info != null && info.Index == tokenIndex);
			if (relevantJsonMemberInfo == null)
				return;

			if (tokenIndex > jTokens.Count)
				return;

			var jToken = jTokens[tokenIndex];
			if (jToken == null)
				return;

			var value = GetTokenValue(relevantJsonMemberInfo.MemberInfo, relevantJsonMemberInfo.MemberType, jToken);
			relevantJsonMemberInfo.SetObjectValue(targetObject, value);
		}

		[CanBeNull]
		private static object GetTokenValue([NotNull] MemberInfo memberInfo, [NotNull] Type type, [NotNull] JToken jToken)
		{
			// Non-numeric values don't need to be specially transformed
			if (jToken.Type != JTokenType.Float && jToken.Type != JTokenType.Integer)
				return jToken.ToObject(type);
			
			// Numeric values might have to be specially transformed depending on the serialization attributes in play
			if(Attribute.IsDefined(memberInfo, typeof (DateTimeSerializesAsUnixTimeSecondsAttribute)))
				return jToken.ToObject<double>().ToDateTime();
			if (Attribute.IsDefined(memberInfo, typeof (TimeSpanSerializesAsMillisecondsAttribute)))
				return TimeSpan.FromMilliseconds(jToken.ToObject<double>());
			if(Attribute.IsDefined(memberInfo, typeof (TimeSpanSerializesAsSecondsAttribute)))
				return TimeSpan.FromSeconds(jToken.ToObject<double>());
			
			// If none of the special serialization attributes were used then we can just return the untransformed value
			return jToken.ToObject(type);
		}

		private class JsonMemberInfo
		{
			public readonly uint Index;
			[CanBeNull]
			public readonly object ExistingValue;
			[NotNull]
			public readonly MemberInfo MemberInfo;
			[NotNull]
			public readonly Type MemberType;

			[NotNull] 
			private readonly Action<object, object> _valueSetter;

			public JsonMemberInfo(uint index, [CanBeNull] object existingValue, [NotNull] MemberInfo memberInfo, [NotNull] Type memberType, [NotNull] Action<object, object> valueSetter)
			{
				Index = index;
				ExistingValue = existingValue;
				MemberInfo = memberInfo;
				MemberType = memberType;
				_valueSetter = valueSetter;
			}

			/// <summary>
			/// Sets the value of one of the members of <code>targetObject</code> to <code>value</code>.
			/// </summary>
			/// <param name="targetObject">The object whose member will have its value set</param>
			/// <param name="value">The value that will be set</param>
			public void SetObjectValue(object targetObject, object value)
			{
				_valueSetter(targetObject, value);
			}
		}
	}
}