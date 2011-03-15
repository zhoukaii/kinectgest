using System;
using System.Linq;
using System.Collections.Generic;

using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace FinalProject
{
	using LabeledGesture = Tuple<string, InputGesture>;
	
	public class LogisticRegressionRecognizer : IRecognizer
	{
		Dictionary<string, List<double>> mWeights;
		double mStepSize;
		double mConvergenceThreshold;
		double mAlpha;
		
		public LogisticRegressionRecognizer()
		{
			mWeights = new Dictionary<string, List<double>>();
			mStepSize = 0.05;
			mConvergenceThreshold = 0.1;
			//mStepSize = 0.1; // For profiling
			//mConvergenceThreshold = 2.0;
			mAlpha = 0.5f;
		}
		
		public string[] Gestures {
			get {
				return mWeights.Keys.ToArray();
			}
		}
	
		struct GestureWeight : IComparable<GestureWeight> {
			public string name;
			public double weight;
			
			public int CompareTo (GestureWeight other) {
				return other.weight.CompareTo(this.weight);
			}
		}
		public RecognizerResult RecognizeSingleGesture (InputGesture g)
		{
			List<IGestureFeature> features = Features.AllFeatures.GestureFeatures;
			
			var results = new List<GestureWeight>();
			foreach ( var kvp in mWeights ) {
				GestureWeight gw = new GestureWeight(){name = kvp.Key};
				for ( int i = 0; i < features.Count; i++ ) {
					float fres = features[i].QueryGesture(g);
					gw.weight += kvp.Value[i] * fres;
				}
				gw.weight += kvp.Value[features.Count];
				gw.weight = Sigmoid(gw.weight);
				results.Add(gw);
			}
			
			results.Sort();
			return new RecognizerResult() {
				Gesture1 = results[0].name, Confidence1 = (float)results[0].weight,
				Gesture2 = results[1].name, Confidence2 = (float)results[1].weight,
				Gesture3 = results[2].name, Confidence3 = (float)results[2].weight
			};
		}
		
		double Sigmoid(double input) {
			return 1.0 / (1.0 + Math.Exp(-input));
		}
		
		double _Sigmoid(List<double> weights, float[] feature_results) {
			double sum = 0.0f;
			for ( int i = 0; i < weights.Count; i++ ) {
				sum += weights[i] * feature_results[i];
			}
			return Sigmoid(sum);
		}
		
		bool _Converged(List<double> oldWeights, List<double> weights, double threshold) {
			for ( int i = 0; i < oldWeights.Count; i++ ) {
				if ( Math.Abs(oldWeights[i] - weights[i]) > threshold ) return false;
			}
			return true;
		}
		
		/// <returns>
		/// The number of iterations it took to learn this class
		/// </returns>
		int LearnGestureClass(float[][] feature_results,
		                      List<Tuple<string, InputGesture>> inputs,
		                      string class_name,
		                      int max_iters) {
			List<IGestureFeature> features = Features.AllFeatures.GestureFeatures;
			mWeights[class_name] = Enumerable.Range(0, features.Count + 1).Select(x => 0.0).ToList(); // The features.Count-indexed value is the "intercept"
			var oldWeights = new List<double>(features.Count + 1);
			
			var sw = new System.Diagnostics.Stopwatch(); sw.Start();
			var num_iters = 0;
			bool converged = true;
			//float diff = 0.0f;
			do {
				if (num_iters > Math.Max(max_iters * 10, 6000)) {
					Console.WriteLine("WARNING: {0} not converging", class_name);
					converged = false;
					break;
				}
				
				oldWeights.Clear();
				oldWeights.AddRange(mWeights[class_name]);
				double wsum = oldWeights.Sum();
				
				for ( int i = 0; i < features.Count + 1; i++ ) {
					var error_sum = 0.0;
					for ( int j = 0; j < inputs.Count; j++ ) {
						var g = inputs[j];
						error_sum += ( ((g.Item1 == class_name) ? 1.0 : 0.0 ) -
					           	     _Sigmoid(mWeights[class_name], feature_results[j]) ) *
							feature_results[j][i];
					}
					error_sum -= wsum * mAlpha;
					mWeights[class_name][i] += mStepSize * error_sum;
				}
				
				num_iters++;
				//diff = _Diff(oldWeights, mWeights[kvp.Key]);
				//Console.WriteLine("Iteration {0}, diff {1}", num_iters, diff);
			} while ( !_Converged(oldWeights, mWeights[class_name], mConvergenceThreshold * mStepSize) );
			
			Console.Write("{0}: {1} iterations in {2}\n  Weights: ", class_name, num_iters, sw.Elapsed);
			mWeights[class_name].ForEach(x => Console.Write("{0:0.000} ", x));
			Console.WriteLine();
			
			if ( converged ) return num_iters;
			return -1;
		}
		
		public void Train (IDictionary<string, IList<InputGesture>> gestures)
		{
			var allgestures = new List<LabeledGesture>();
			foreach ( var kvp in gestures ) {
				foreach ( var instance in kvp.Value ) {
					allgestures.Add(new Tuple<string, InputGesture>(kvp.Key, instance));
				}
			}
			
			
			var feature_results = new float[gestures.Values.Sum(x => x.Count)][];
			for ( int i = 0; i < allgestures.Count; i++ ) {
				var temp = Features.AllFeatures.GestureFeatureResults(allgestures[i].Item2).ToList();
				temp.Add(1.0f);
				feature_results[i] = temp.ToArray();
			}
			
			int max_iters = 0;
			foreach ( var kvp in gestures ) {
				int num_iters = LearnGestureClass(feature_results,
				                                  allgestures,
				                                  kvp.Key,
				                                  max_iters);
				max_iters = Math.Max(num_iters, max_iters);
			}
		}

		public void SaveModel (string filename)
		{
			var stream = File.Open(filename, FileMode.Create);
			var formatter = new BinaryFormatter();
			formatter.Serialize(stream, this.GetType());
			formatter.Serialize(stream, mWeights);
			stream.Close();
			
			Console.WriteLine("Saved trained model to {0}", filename);
		}

		public void LoadModel (string filename)
		{
			var stream = File.Open(filename, FileMode.Open);
			var formatter = new BinaryFormatter();
			var mtype = (Type)formatter.Deserialize(stream);
			if ( !mtype.Equals(this.GetType()) ) throw new InvalidDataException();
			mWeights = (Dictionary<string, List<double>>)formatter.Deserialize(stream);
			stream.Close();
			
			Console.WriteLine("Loaded model from {0}", filename);
		}

		public void ClearHistory ()
		{
			throw new NotImplementedException ();
		}

		public RecognizerResult AddNewData (JointState js)
		{
			throw new NotImplementedException ();
		}
	}
}
